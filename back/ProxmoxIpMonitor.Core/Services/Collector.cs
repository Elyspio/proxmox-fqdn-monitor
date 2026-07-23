using System.Diagnostics;
using Elyspio.Utils.Telemetry.Tracing.Elements;
using Microsoft.Extensions.Logging;
using ProxmoxIpMonitor.Abstractions.Interfaces;
using ProxmoxIpMonitor.Abstractions.Models;

namespace ProxmoxIpMonitor.Core.Services;

/// <summary>
///     Polls every enabled node, merges the result into the stored snapshot, and reconciles DNS.
///     A single semaphore serialises every entry point. Two concurrent cycles would race on the
///     snapshot and, worse, issue overlapping writes to the same DNS zone — so a manual trigger
///     waits for the scheduled cycle rather than running beside it.
/// </summary>
public sealed class Collector(
	INodeRepository nodes,
	IHostRepository hosts,
	IIpEventRepository ipEvents,
	ICollectionRunRepository collectionRuns,
	IDnsPushRepository dnsPushes,
	ISettingsRepository settingsRepository,
	IPveClient pveClient,
	IEnumerable<IDnsProvider> dnsProviders,
	ILogger<Collector> logger) : TracingService(logger), ICollector
{
	private readonly SemaphoreSlim _gate = new(1, 1);

	/// <inheritdoc />
	public async Task<IReadOnlyList<CollectionRun>> CollectNowAsync(CancellationToken ct = default)
	{
		using var trace = LogService();

		await _gate.WaitAsync(ct);
		try
		{
			var runs = await CollectCoreAsync(ct);
			await ReconcileCoreAsync(ct);
			return runs;
		}
		finally
		{
			_gate.Release();
		}
	}

	/// <inheritdoc />
	public async Task<IReadOnlyList<DnsPush>> PushDnsAsync(CancellationToken ct = default)
	{
		using var trace = LogService();

		await _gate.WaitAsync(ct);
		try
		{
			return await ReconcileCoreAsync(ct);
		}
		finally
		{
			_gate.Release();
		}
	}

	/// <inheritdoc />
	public async Task<IReadOnlyList<DnsState>> InspectDnsAsync(CancellationToken ct = default)
	{
		using var trace = LogService();

		// Read-only: deliberately not gated, so opening the DNS screen never waits on a poll.
		var settings = await settingsRepository.GetAsync(ct);
		var snapshot = await hosts.GetAllAsync(ct);
		var desired = DesiredRecordBuilder.Build(snapshot, settings);

		var states = new List<DnsState>();
		foreach (var provider in dnsProviders)
			states.Add(await provider.InspectAsync(settings, desired, ct));

		return states;
	}

	private async Task<IReadOnlyList<CollectionRun>> CollectCoreAsync(CancellationToken ct)
	{
		var settings = await settingsRepository.GetAsync(ct);
		var allNodes = await nodes.GetAllAsync(ct);
		var enabled = allNodes.Where(node => node.Enabled && node.Id is not null).ToArray();

		if (enabled.Length == 0)
		{
			logger.LogDebug("No enabled Proxmox node configured; skipping collection.");
			return [];
		}

		// Nodes are polled in parallel and their failures are independent: one unreachable
		// hypervisor must not stop the other from refreshing.
		var results = await Task.WhenAll(enabled.Select(node => PollAsync(node, settings, ct)));

		var runs = results.Select(result => result.Run).ToArray();
		var discovered = results
			.Where(result => result.Snapshot is not null)
			.ToDictionary(result => result.Node.Id!, result => result.Snapshot!.Hosts);

		var polledNodeIds = discovered.Keys.ToHashSet(StringComparer.Ordinal);
		var nodeNames = enabled.ToDictionary(node => node.Id!, node => node.DisplayName);

		var stored = await hosts.GetAllAsync(ct);

		// Hosts belonging to a node that was deleted from the configuration have no owner left.
		var knownNodeIds = allNodes.Select(node => node.Id!).ToHashSet(StringComparer.Ordinal);
		var orphaned = stored.Where(host => !knownNodeIds.Contains(host.NodeId)).Select(host => host.Key).ToArray();

		var diff = SnapshotDiffer.Diff(
			stored,
			discovered,
			polledNodeIds,
			nodeNames,
			TimeSpan.FromMinutes(Math.Max(1, settings.RetentionMinutes)),
			DateTime.UtcNow);

		await hosts.UpsertManyAsync(diff.Upserts, ct);
		await hosts.DeleteManyAsync([..diff.Deletions, ..orphaned], ct);
		await ipEvents.AppendManyAsync(diff.Events, ct);

		foreach (var run in runs) await collectionRuns.AppendAsync(run, ct);

		if (diff.Events.Count > 0)
			logger.LogInformation("Collection recorded {Count} address change(s)", diff.Events.Count);

		return runs;
	}

	private async Task<PollResult> PollAsync(PveNode node, AppSettings settings, CancellationToken ct)
	{
		var startedAt = DateTime.UtcNow;
		var stopwatch = Stopwatch.GetTimestamp();

		var cidrs = settings.SubnetsFilter.Select(subnet => subnet.Cidr).ToArray();
		var result = await pveClient.CollectAsync(node, cidrs, ct);

		if (!result.Success)
		{
			logger.LogWarning(result.Error, "Collection failed for node {Node}", node.DisplayName);

			return new PollResult(node, null, new CollectionRun
			{
				NodeId = node.Id!,
				NodeName = node.DisplayName,
				StartedAt = startedAt,
				Duration = Stopwatch.GetElapsedTime(stopwatch),
				Outcome = CollectionOutcome.Failed,
				HostsDiscovered = 0,
				HostsWithIp = 0,
				Errors = [result.Error.Message]
			});
		}

		var snapshot = result.Data;
		var withIp = snapshot.Hosts.Count(host => host.Ip is not null);

		var issues = snapshot.Hosts
			.Where(host => host.Ip is null)
			.Select(host => new HostIssue
			{
				VmId = host.VmId,
				Hostname = host.Hostname,
				Type = host.Type,
				Reason = host.Issue ?? "No address reported"
			})
			.ToArray();

		return new PollResult(node, snapshot, new CollectionRun
		{
			NodeId = node.Id!,
			NodeName = node.DisplayName,
			StartedAt = startedAt,
			Duration = Stopwatch.GetElapsedTime(stopwatch),
			Outcome = issues.Length == 0 ? CollectionOutcome.Succeeded : CollectionOutcome.Partial,
			HostsDiscovered = snapshot.Hosts.Count,
			HostsWithIp = withIp,
			Issues = issues
		});
	}

	private async Task<IReadOnlyList<DnsPush>> ReconcileCoreAsync(CancellationToken ct)
	{
		var settings = await settingsRepository.GetAsync(ct);
		var snapshot = await hosts.GetAllAsync(ct);
		var desired = DesiredRecordBuilder.Build(snapshot, settings);

		var pushes = new List<DnsPush>();

		foreach (var provider in dnsProviders)
		{
			if (!provider.IsEnabled(settings)) continue;

			// When reconciliation is off the pass still runs, but as a dry run: the operator gets
			// the diff in the UI without the zone being touched. That is the intended first run.
			var push = await provider.ReconcileAsync(settings, desired, !settings.ReconciliationEnabled, ct);

			await dnsPushes.AppendAsync(push, ct);
			pushes.Add(push);
		}

		return pushes;
	}

	private sealed record PollResult(PveNode Node, NodeSnapshot? Snapshot, CollectionRun Run);
}
