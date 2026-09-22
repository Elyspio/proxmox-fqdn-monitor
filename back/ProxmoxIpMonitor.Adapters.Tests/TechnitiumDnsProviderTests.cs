using System.Web;
using Microsoft.Extensions.Logging.Abstractions;
using ProxmoxIpMonitor.Abstractions.Models;
using ProxmoxIpMonitor.Adapters.Dns.Technitium;
using Shouldly;
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

		var add = handler.Requests.Where(r => r.Path.EndsWith("records/add")).ShouldHaveSingleItem();
		var form = FormOf(add);

		form["comments"].ShouldBe(Marker);
		form["domain"].ShouldBe("web-01.elylan");
		form["ipAddress"].ShouldBe("10.0.10.5");
		form["ttl"].ShouldBe("300");
		form["overwrite"].ShouldBe("true");
		form["node"].ShouldBe("ely-dns-01.elylan");
	}

	[Fact]
	public async Task ExpiryTtlIsNeverSent()
	{
		// Record aging forces a rewrite on every run, which is what bloated the primary zone's
		// IXFR history in the previous exporter. Stale records are deleted explicitly instead.
		var (provider, handler) = Build(ZoneJson());

		await provider.ReconcileAsync(Settings(), [new DesiredRecord("web-01", "10.0.10.5")], false, Ct);

		var add = handler.Requests.Where(r => r.Path.EndsWith("records/add")).ShouldHaveSingleItem();
		FormOf(add).Keys.ShouldNotContain("expiryTtl");
	}

	[Fact]
	public async Task AMatchingOwnedRecordIsLeftAlone()
	{
		var (provider, handler) = Build(ZoneJson(("web-01.elylan", "10.0.10.5", 300, Marker)));

		var push = await provider.ReconcileAsync(Settings(), [new DesiredRecord("web-01", "10.0.10.5")], false, Ct);

		push.Skipped.ShouldBe(1);
		push.Written.ShouldBe(0);
		handler.Requests.ShouldNotContain(r => r.Path.EndsWith("records/add"));
	}

	[Fact]
	public async Task AMatchingRecordWithoutTheMarkerIsAdoptedOnTheFirstRun()
	{
		// Records left behind by the previous exporter have no marker. Rewriting them once is
		// what brings them under management instead of stranding them forever.
		var (provider, handler) = Build(ZoneJson(("web-01.elylan", "10.0.10.5", 300, null)));

		var push = await provider.ReconcileAsync(Settings(), [new DesiredRecord("web-01", "10.0.10.5")], false, Ct);

		push.Written.ShouldBe(1);
		var add = handler.Requests.Where(r => r.Path.EndsWith("records/add")).ShouldHaveSingleItem();
		(FormOf(add)["comments"]).ShouldBe(Marker);
	}

	[Fact]
	public async Task OrphanedOwnedRecordsAreDeleted()
	{
		var (provider, handler) = Build(ZoneJson(("gone.elylan", "10.0.10.99", 300, Marker)));

		var push = await provider.ReconcileAsync(Settings(), [], false, Ct);

		push.Deleted.ShouldBe(1);
		var delete = handler.Requests.Where(r => r.Path.EndsWith("records/delete")).ShouldHaveSingleItem();
		var form = FormOf(delete);
		form["domain"].ShouldBe("gone.elylan");
		form["ipAddress"].ShouldBe("10.0.10.99");
		form["type"].ShouldBe("A");
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

		push.Deleted.ShouldBe(0);
		handler.Requests.ShouldNotContain(r => r.Path.EndsWith("records/delete"));
	}

	[Fact]
	public async Task UnmanagedRecordsAreReportedSoTheyAreVisibleRatherThanIgnored()
	{
		var (provider, _) = Build(ZoneJson(("proxy.elylan", "10.0.0.100", 3600, null)));

		var state = await provider.InspectAsync(Settings(), [], Ct);

		state.Unmanaged.ShouldHaveSingleItem().Domain.ShouldBe("proxy.elylan");
		state.Orphans.ShouldBeEmpty();
	}

	[Fact]
	public async Task DryRunReportsTheDiffWithoutTouchingTheZone()
	{
		var (provider, handler) = Build(ZoneJson(("gone.elylan", "10.0.10.99", 300, Marker)));

		var push = await provider.ReconcileAsync(Settings(), [new DesiredRecord("web-01", "10.0.10.5")], true, Ct);

		push.DryRun.ShouldBeTrue();
		push.Written.ShouldBe(1);
		push.Deleted.ShouldBe(1);
		handler.Requests.ShouldNotContain(r => r.Path.EndsWith("records/add"));
		handler.Requests.ShouldNotContain(r => r.Path.EndsWith("records/delete"));
	}

	[Fact]
	public async Task OrphansAreKeptWhenDeletionIsDisabled()
	{
		var (provider, handler) = Build(ZoneJson(("gone.elylan", "10.0.10.99", 300, Marker)));

		var push = await provider.ReconcileAsync(Settings(deleteOrphans: false), [], false, Ct);

		push.Deleted.ShouldBe(0);
		handler.Requests.ShouldNotContain(r => r.Path.EndsWith("records/delete"));
	}

	[Fact]
	public async Task ATtlChangeRewritesTheRecord()
	{
		var (provider, _) = Build(ZoneJson(("web-01.elylan", "10.0.10.5", 60, Marker)));

		var push = await provider.ReconcileAsync(Settings(), [new DesiredRecord("web-01", "10.0.10.5")], false, Ct);

		push.Written.ShouldBe(1);
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

		push.Failed.ShouldBe(1);
		push.Errors.ShouldHaveSingleItem().ShouldContain("zone is locked");
	}

	[Theory]
	[InlineData("web-01", "elylan", "web-01.elylan")]
	[InlineData("web-01.elylan", "elylan", "web-01.elylan")]
	[InlineData("elylan", "elylan", "elylan")]
	[InlineData("web-01.", "elylan", "web-01.elylan")]
	public void HostnamesAreQualifiedWithTheZoneExactlyOnce(string hostname, string zone, string expected)
	{
		TechnitiumDnsProvider.BuildDomainName(hostname, zone).ShouldBe(expected);
	}

	[Fact]
	public async Task ADisabledProviderWritesNothing()
	{
		var (provider, handler) = Build(ZoneJson());
		var settings = Settings() with { Technitium = Settings().Technitium with { Enabled = false } };

		var push = await provider.ReconcileAsync(settings, [new DesiredRecord("web-01", "10.0.10.5")], false, Ct);

		handler.Requests.ShouldBeEmpty();
		push.Errors.ShouldHaveSingleItem();
	}
}
