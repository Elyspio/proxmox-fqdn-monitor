using System.Web;
using Microsoft.Extensions.Logging.Abstractions;
using ProxmoxIpMonitor.Abstractions.Models;
using ProxmoxIpMonitor.Adapters.Dns.Technitium;
using Xunit;

namespace ProxmoxIpMonitor.Adapters.Tests;

/// <summary>
///     These tests exist to keep two promises the design rests on: records this tool writes are
///     tagged as its own, and records it did not write are never modified or deleted.
/// </summary>
public class TechnitiumDnsProviderTests
{
	private const string Marker = TechnitiumDnsProvider.OwnershipMarker;

	/// <summary>Test cancellation token, so a hung adapter call fails the run instead of stalling it.</summary>
	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	private static AppSettings Settings(bool reconcile = true, bool deleteOrphans = true)
	{
		return new AppSettings
		{
			ReconciliationEnabled = reconcile,
			DeleteOrphanRecords = deleteOrphans,
			Technitium = new TechnitiumSettings
			{
				Enabled = true,
				BaseUrl = "http://dns.test:5380",
				ApiTokenProtected = "token",
				Zone = "elylan",
				PrimaryNode = "ely-dns-01.elylan",
				RecordTtlSeconds = 300
			}
		};
	}

	/// <summary>Builds a zone listing response out of (name, ip, ttl, comments) tuples.</summary>
	private static string ZoneJson(params (string Name, string Ip, int Ttl, string? Comments)[] records)
	{
		var entries = records.Select(r =>
		{
			var comments = r.Comments is null ? "" : ",\"comments\":\"" + r.Comments + "\"";
			return "{\"name\":\"" + r.Name + "\",\"type\":\"A\",\"ttl\":" + r.Ttl
			       + ",\"rData\":{\"ipAddress\":\"" + r.Ip + "\"}" + comments + "}";
		});

		return "{\"status\":\"ok\",\"response\":{\"records\":[" + string.Join(",", entries) + "]}}";
	}

	private static (TechnitiumDnsProvider Provider, FakeHttpMessageHandler Handler) Build(string zoneJson)
	{
		var handler = new FakeHttpMessageHandler((request, _) =>
			request.RequestUri!.AbsolutePath.Contains("records/get")
				? FakeHttpMessageHandler.Json(zoneJson)
				: FakeHttpMessageHandler.Json("""{"status":"ok"}"""));

		var provider = new TechnitiumDnsProvider(
			new FakeHttpClientFactory(handler),
			new PassthroughProtector(),
			NullLogger<TechnitiumDnsProvider>.Instance);

		return (provider, handler);
	}

	private static Dictionary<string, string> FormOf(CapturedRequest request)
	{
		var parsed = HttpUtility.ParseQueryString(request.Body);
		return parsed.AllKeys.Where(k => k is not null).ToDictionary(k => k!, k => parsed[k] ?? "");
	}

	[Fact]
	public async Task WrittenRecordsCarryTheOwnershipMarker()
	{
		var (provider, handler) = Build(ZoneJson());

		await provider.ReconcileAsync(Settings(), [new DesiredRecord("web-01", "10.0.10.5")], false, Ct);

		var add = Assert.Single(handler.Requests, r => r.Path.EndsWith("records/add"));
		var form = FormOf(add);

		Assert.Equal(Marker, form["comments"]);
		Assert.Equal("web-01.elylan", form["domain"]);
		Assert.Equal("10.0.10.5", form["ipAddress"]);
		Assert.Equal("300", form["ttl"]);
		Assert.Equal("true", form["overwrite"]);
		Assert.Equal("ely-dns-01.elylan", form["node"]);
	}

	[Fact]
	public async Task ExpiryTtlIsNeverSent()
	{
		// Record aging forces a rewrite on every run, which is what bloated the primary zone's
		// IXFR history in the previous exporter. Stale records are deleted explicitly instead.
		var (provider, handler) = Build(ZoneJson());

		await provider.ReconcileAsync(Settings(), [new DesiredRecord("web-01", "10.0.10.5")], false, Ct);

		var add = Assert.Single(handler.Requests, r => r.Path.EndsWith("records/add"));
		Assert.DoesNotContain("expiryTtl", FormOf(add).Keys);
	}

	[Fact]
	public async Task AMatchingOwnedRecordIsLeftAlone()
	{
		var (provider, handler) = Build(ZoneJson(("web-01.elylan", "10.0.10.5", 300, Marker)));

		var push = await provider.ReconcileAsync(Settings(), [new DesiredRecord("web-01", "10.0.10.5")], false, Ct);

		Assert.Equal(1, push.Skipped);
		Assert.Equal(0, push.Written);
		Assert.DoesNotContain(handler.Requests, r => r.Path.EndsWith("records/add"));
	}

	[Fact]
	public async Task AMatchingRecordWithoutTheMarkerIsAdoptedOnTheFirstRun()
	{
		// Records left behind by the previous exporter have no marker. Rewriting them once is
		// what brings them under management instead of stranding them forever.
		var (provider, handler) = Build(ZoneJson(("web-01.elylan", "10.0.10.5", 300, null)));

		var push = await provider.ReconcileAsync(Settings(), [new DesiredRecord("web-01", "10.0.10.5")], false, Ct);

		Assert.Equal(1, push.Written);
		var add = Assert.Single(handler.Requests, r => r.Path.EndsWith("records/add"));
		Assert.Equal(Marker, FormOf(add)["comments"]);
	}

	[Fact]
	public async Task OrphanedOwnedRecordsAreDeleted()
	{
		var (provider, handler) = Build(ZoneJson(("gone.elylan", "10.0.10.99", 300, Marker)));

		var push = await provider.ReconcileAsync(Settings(), [], false, Ct);

		Assert.Equal(1, push.Deleted);
		var delete = Assert.Single(handler.Requests, r => r.Path.EndsWith("records/delete"));
		var form = FormOf(delete);
		Assert.Equal("gone.elylan", form["domain"]);
		Assert.Equal("10.0.10.99", form["ipAddress"]);
		Assert.Equal("A", form["type"]);
	}

	[Fact]
	public async Task RecordsWithoutTheMarkerAreNeverDeleted()
	{
		// The zone also holds hand-maintained records (ely-dns-01, proxy, npm.packages...).
		// Reconciliation must be structurally incapable of removing them.
		var (provider, handler) = Build(ZoneJson(
			("proxy.elylan", "10.0.0.100", 3600, null),
			("ely-dns-01.elylan", "10.0.10.241", 3600, "manual"),
			("npm.packages.elylan", "10.0.0.100", 3600, null)));

		var push = await provider.ReconcileAsync(Settings(), [], false, Ct);

		Assert.Equal(0, push.Deleted);
		Assert.DoesNotContain(handler.Requests, r => r.Path.EndsWith("records/delete"));
	}

	[Fact]
	public async Task UnmanagedRecordsAreReportedSoTheyAreVisibleRatherThanIgnored()
	{
		var (provider, _) = Build(ZoneJson(("proxy.elylan", "10.0.0.100", 3600, null)));

		var state = await provider.InspectAsync(Settings(), [], Ct);

		Assert.Equal("proxy.elylan", Assert.Single(state.Unmanaged).Domain);
		Assert.Empty(state.Orphans);
	}

	[Fact]
	public async Task DryRunReportsTheDiffWithoutTouchingTheZone()
	{
		var (provider, handler) = Build(ZoneJson(("gone.elylan", "10.0.10.99", 300, Marker)));

		var push = await provider.ReconcileAsync(Settings(), [new DesiredRecord("web-01", "10.0.10.5")], true, Ct);

		Assert.True(push.DryRun);
		Assert.Equal(1, push.Written);
		Assert.Equal(1, push.Deleted);
		Assert.DoesNotContain(handler.Requests, r => r.Path.EndsWith("records/add"));
		Assert.DoesNotContain(handler.Requests, r => r.Path.EndsWith("records/delete"));
	}

	[Fact]
	public async Task OrphansAreKeptWhenDeletionIsDisabled()
	{
		var (provider, handler) = Build(ZoneJson(("gone.elylan", "10.0.10.99", 300, Marker)));

		var push = await provider.ReconcileAsync(Settings(deleteOrphans: false), [], false, Ct);

		Assert.Equal(0, push.Deleted);
		Assert.DoesNotContain(handler.Requests, r => r.Path.EndsWith("records/delete"));
	}

	[Fact]
	public async Task ATtlChangeRewritesTheRecord()
	{
		var (provider, _) = Build(ZoneJson(("web-01.elylan", "10.0.10.5", 60, Marker)));

		var push = await provider.ReconcileAsync(Settings(), [new DesiredRecord("web-01", "10.0.10.5")], false, Ct);

		Assert.Equal(1, push.Written);
	}

	[Fact]
	public async Task AFailedWriteIsReportedRatherThanThrown()
	{
		var handler = new FakeHttpMessageHandler((request, _) =>
			request.RequestUri!.AbsolutePath.Contains("records/get")
				? FakeHttpMessageHandler.Json(ZoneJson())
				: FakeHttpMessageHandler.Json("""{"status":"error","errorMessage":"zone is locked"}"""));

		var provider = new TechnitiumDnsProvider(
			new FakeHttpClientFactory(handler),
			new PassthroughProtector(),
			NullLogger<TechnitiumDnsProvider>.Instance);

		var push = await provider.ReconcileAsync(Settings(), [new DesiredRecord("web-01", "10.0.10.5")], false, Ct);

		Assert.Equal(1, push.Failed);
		Assert.Contains("zone is locked", Assert.Single(push.Errors));
	}

	[Theory]
	[InlineData("web-01", "elylan", "web-01.elylan")]
	[InlineData("web-01.elylan", "elylan", "web-01.elylan")]
	[InlineData("elylan", "elylan", "elylan")]
	[InlineData("web-01.", "elylan", "web-01.elylan")]
	public void HostnamesAreQualifiedWithTheZoneExactlyOnce(string hostname, string zone, string expected)
	{
		Assert.Equal(expected, TechnitiumDnsProvider.BuildDomainName(hostname, zone));
	}

	[Fact]
	public async Task ADisabledProviderWritesNothing()
	{
		var (provider, handler) = Build(ZoneJson());
		var settings = Settings() with { Technitium = Settings().Technitium with { Enabled = false } };

		var push = await provider.ReconcileAsync(settings, [new DesiredRecord("web-01", "10.0.10.5")], false, Ct);

		Assert.Empty(handler.Requests);
		Assert.Single(push.Errors);
	}
}
