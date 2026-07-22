using System.Diagnostics;
using Elyspio.Utils.Telemetry.Technical.Helpers;
using Elyspio.Utils.Telemetry.Tracing.Elements;
using Microsoft.Extensions.Logging;
using ProxmoxIpMonitor.Abstractions.Interfaces;
using ProxmoxIpMonitor.Abstractions.Models;

namespace ProxmoxIpMonitor.Adapters.Dns.Technitium;

/// <summary>
///     Reconciles a Technitium primary zone against the desired record set.
///     Two invariants govern everything here:
///     records this tool writes carry <see cref="OwnershipMarker" /> in their comments, and
///     nothing without that marker is ever written over or deleted. That is what makes it safe
///     to point this at a zone that also holds hand-maintained records.
/// </summary>
public sealed class TechnitiumDnsProvider(
	IHttpClientFactory httpClientFactory,
	ISecretProtector protector,
	ILogger<TechnitiumDnsProvider> logger) : TracingAdapter(logger), IDnsProvider
{
	/// <summary>Written into every managed record's comments field. Changing it orphans the existing zone.</summary>
	public const string OwnershipMarker = "proxmox-ip-monitor";

	public string Name => "Technitium";

	public bool IsEnabled(AppSettings settings)
	{
		using var trace = LogAdapter();

		var technitium = settings.Technitium;
		return technitium.Enabled
		       && !string.IsNullOrWhiteSpace(technitium.BaseUrl)
		       && !string.IsNullOrWhiteSpace(technitium.Zone)
		       && !string.IsNullOrWhiteSpace(technitium.ApiTokenProtected);
	}

	/// <inheritdoc />
	public async Task<DnsState> InspectAsync(AppSettings settings, IReadOnlyCollection<DesiredRecord> desired, CancellationToken ct = default)
	{
		using var trace = LogAdapter($"{Log.F(settings.Technitium.Zone)} {Log.F(desired.Count)}");

		if (!IsEnabled(settings))
			return new DnsState { Enabled = false, Zone = settings.Technitium.Zone };

		try
		{
			var plan = await BuildPlanAsync(settings, desired, ct);
			return new DnsState
			{
				Enabled = true,
				Zone = settings.Technitium.Zone,
				UpToDate = plan.UpToDate,
				ToWrite = plan.ToWrite,
				Orphans = plan.Orphans,
				Unmanaged = plan.Unmanaged
			};
		}
		catch (Exception e)
		{
			logger.LogError(e, "Failed to inspect Technitium zone {Zone}", settings.Technitium.Zone);
			return new DnsState { Enabled = true, Zone = settings.Technitium.Zone, Error = e.Message };
		}
	}

	/// <inheritdoc />
	public async Task<DnsPush> ReconcileAsync(AppSettings settings, IReadOnlyCollection<DesiredRecord> desired, bool dryRun, CancellationToken ct = default)
	{
		using var trace = LogAdapter($"{Log.F(settings.Technitium.Zone)} {Log.F(desired.Count)} {Log.F(dryRun)}");

		var startedAt = DateTime.UtcNow;
		var stopwatch = Stopwatch.GetTimestamp();
		var outcomes = new List<DnsRecordOutcome>();
		var errors = new List<string>();

		if (!IsEnabled(settings))
			return Build(startedAt, stopwatch, dryRun, outcomes, ["Technitium export is disabled or incompletely configured."]);

		var config = settings.Technitium;
		string apiToken;
		Plan plan;

		try
		{
			apiToken = protector.Unprotect(config.ApiTokenProtected);
			plan = await BuildPlanAsync(settings, desired, ct);
		}
		catch (Exception e)
		{
			logger.LogError(e, "Failed to prepare Technitium reconciliation for zone {Zone}", config.Zone);
			return Build(startedAt, stopwatch, dryRun, outcomes, [e.Message]);
		}

		foreach (var record in plan.UpToDate)
			outcomes.Add(new DnsRecordOutcome { Domain = record.Domain, Ip = record.Ip, Action = DnsRecordAction.Skipped });

		var api = new TechnitiumApi(httpClientFactory.CreateClient(nameof(TechnitiumDnsProvider)), logger);

		foreach (var record in plan.ToWrite)
		{
			if (dryRun)
			{
				outcomes.Add(new DnsRecordOutcome { Domain = record.Domain, Ip = record.Ip, Action = DnsRecordAction.Written });
				continue;
			}

			try
			{
				await api.AddOrUpdateAsync(config, apiToken, record, OwnershipMarker, ct);
				outcomes.Add(new DnsRecordOutcome { Domain = record.Domain, Ip = record.Ip, Action = DnsRecordAction.Written });
				logger.LogDebug("Upserted A record {Domain} -> {Ip}", record.Domain, record.Ip);
			}
			catch (Exception e)
			{
				outcomes.Add(new DnsRecordOutcome { Domain = record.Domain, Ip = record.Ip, Action = DnsRecordAction.Failed, Error = e.Message });
				errors.Add($"{record.Domain}: {e.Message}");
			}
		}

		// Orphans only ever contains records that carried the ownership marker — see BuildPlanAsync.
		if (settings.DeleteOrphanRecords)
			foreach (var orphan in plan.Orphans)
			{
				if (dryRun)
				{
					outcomes.Add(new DnsRecordOutcome { Domain = orphan.Domain, Ip = orphan.Ip, Action = DnsRecordAction.Deleted });
					continue;
				}

				try
				{
					await api.DeleteAsync(config, apiToken, orphan, ct);
					outcomes.Add(new DnsRecordOutcome { Domain = orphan.Domain, Ip = orphan.Ip, Action = DnsRecordAction.Deleted });
					logger.LogInformation("Deleted orphan A record {Domain} -> {Ip}", orphan.Domain, orphan.Ip);
				}
				catch (Exception e)
				{
					outcomes.Add(new DnsRecordOutcome { Domain = orphan.Domain, Ip = orphan.Ip, Action = DnsRecordAction.Failed, Error = e.Message });
					errors.Add($"{orphan.Domain}: {e.Message}");
				}
			}

		var push = Build(startedAt, stopwatch, dryRun, outcomes, errors);

		logger.LogInformation(
			"Technitium reconciliation for zone {Zone} ({Mode}): {Written} written, {Skipped} unchanged, {Deleted} deleted, {Failed} failed, {Unmanaged} left untouched",
			config.Zone, dryRun ? "dry run" : "applied", push.Written, push.Skipped, push.Deleted, push.Failed, plan.Unmanaged.Count);

		return push;
	}

	/// <summary>
	///     Splits the live zone against the desired set.
	///     A desired record is considered up to date only when the live record matches on address
	///     and TTL <em>and</em> already carries the marker. That last condition is what makes the
	///     first run adopt records left behind by the previous exporter instead of ignoring them.
	/// </summary>
	private async Task<Plan> BuildPlanAsync(AppSettings settings, IReadOnlyCollection<DesiredRecord> desired, CancellationToken ct)
	{
		var config = settings.Technitium;
		var apiToken = protector.Unprotect(config.ApiTokenProtected);
		var api = new TechnitiumApi(httpClientFactory.CreateClient(nameof(TechnitiumDnsProvider)), logger);

		var existing = await api.ListARecordsAsync(config, apiToken, ct);

		var normalizedDesired = desired
			.Select(record => new DesiredRecord(BuildDomainName(record.Domain, config.Zone), record.Ip))
			.DistinctBy(record => record.Domain, StringComparer.OrdinalIgnoreCase)
			.OrderBy(record => record.Domain, StringComparer.OrdinalIgnoreCase)
			.ToArray();

		var byDomain = existing
			.GroupBy(record => record.Domain, StringComparer.OrdinalIgnoreCase)
			.ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

		var upToDate = new List<DesiredRecord>();
		var toWrite = new List<DesiredRecord>();

		foreach (var record in normalizedDesired)
		{
			var current = byDomain.GetValueOrDefault(record.Domain);

			// A single matching record, at the right TTL, already marked as ours.
			var satisfied = current is { Count: 1 }
			                && string.Equals(current[0].Ip, record.Ip, StringComparison.OrdinalIgnoreCase)
			                && current[0].Ttl == config.RecordTtlSeconds
			                && IsOwned(current[0]);

			if (satisfied) upToDate.Add(record);
			else toWrite.Add(record);
		}

		var desiredDomains = normalizedDesired.Select(record => record.Domain).ToHashSet(StringComparer.OrdinalIgnoreCase);

		var orphans = existing
			.Where(record => IsOwned(record) && !desiredDomains.Contains(record.Domain))
			.OrderBy(record => record.Domain, StringComparer.OrdinalIgnoreCase)
			.ToList();

		// Everything else in the zone was put there by someone else. It is surfaced so the UI can
		// show it, and never touched.
		var unmanaged = existing
			.Where(record => !IsOwned(record) && !desiredDomains.Contains(record.Domain))
			.OrderBy(record => record.Domain, StringComparer.OrdinalIgnoreCase)
			.ToList();

		return new Plan(upToDate, toWrite, orphans, unmanaged);
	}

	internal static bool IsOwned(ExistingRecord record)
	{
		return record.Comments is not null
		       && record.Comments.Contains(OwnershipMarker, StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>Qualifies a bare hostname with the zone, leaving already-qualified names alone.</summary>
	internal static string BuildDomainName(string hostname, string zone)
	{
		var normalizedHostname = hostname.Trim().TrimEnd('.');
		var normalizedZone = zone.Trim().Trim('.');

		if (normalizedHostname.EndsWith($".{normalizedZone}", StringComparison.OrdinalIgnoreCase)
		    || string.Equals(normalizedHostname, normalizedZone, StringComparison.OrdinalIgnoreCase))
			return normalizedHostname;

		return $"{normalizedHostname}.{normalizedZone}";
	}

	private DnsPush Build(DateTime startedAt, long stopwatch, bool dryRun, List<DnsRecordOutcome> outcomes, IReadOnlyList<string> errors)
	{
		return new DnsPush
		{
			Provider = Name,
			At = startedAt,
			Duration = Stopwatch.GetElapsedTime(stopwatch),
			DryRun = dryRun,
			Written = outcomes.Count(o => o.Action == DnsRecordAction.Written),
			Skipped = outcomes.Count(o => o.Action == DnsRecordAction.Skipped),
			Deleted = outcomes.Count(o => o.Action == DnsRecordAction.Deleted),
			Failed = outcomes.Count(o => o.Action == DnsRecordAction.Failed),
			Outcomes = outcomes,
			Errors = errors
		};
	}

	private sealed record Plan(
		IReadOnlyList<DesiredRecord> UpToDate,
		IReadOnlyList<DesiredRecord> ToWrite,
		IReadOnlyList<ExistingRecord> Orphans,
		IReadOnlyList<ExistingRecord> Unmanaged);
}
