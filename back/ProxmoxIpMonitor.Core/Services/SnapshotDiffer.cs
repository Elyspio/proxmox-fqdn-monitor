using ProxmoxIpMonitor.Abstractions.Interfaces;
using ProxmoxIpMonitor.Abstractions.Models;

namespace ProxmoxIpMonitor.Core.Services;

/// <summary>Result of merging one poll into the stored snapshot.</summary>
public sealed record DiffResult(
	IReadOnlyList<MonitoredHost> Upserts,
	IReadOnlyList<string> Deletions,
	IReadOnlyList<IpEvent> Events);

/// <summary>
///     Merges what a poll discovered into what is already stored, and derives the history events.
///     Pure and side-effect free so the retention and change-detection rules can be tested
///     without Mongo or a Proxmox node.
/// </summary>
public static class SnapshotDiffer
{
	/// <param name="stored">Every host currently persisted, across all nodes.</param>
	/// <param name="discovered">What this cycle observed, keyed by node id.</param>
	/// <param name="polledNodeIds">Nodes that answered. Hosts of a node that failed are left untouched.</param>
	/// <param name="nodeNames">Display name per node id, denormalised onto the stored host.</param>
	/// <param name="retention">How long a vanished host keeps its record.</param>
	/// <param name="now">Clock, injected so tests can move time.</param>
	public static DiffResult Diff(
		IReadOnlyCollection<MonitoredHost> stored,
		IReadOnlyDictionary<string, IReadOnlyList<DiscoveredHost>> discovered,
		IReadOnlyCollection<string> polledNodeIds,
		IReadOnlyDictionary<string, string> nodeNames,
		TimeSpan retention,
		DateTime now)
	{
		var storedByKey = stored.ToDictionary(host => host.Key, StringComparer.Ordinal);
		var upserts = new List<MonitoredHost>();
		var events = new List<IpEvent>();
		var seenKeys = new HashSet<string>(StringComparer.Ordinal);

		foreach (var (nodeId, hosts) in discovered)
		{
			var nodeName = nodeNames.GetValueOrDefault(nodeId, nodeId);

			foreach (var host in hosts)
			{
				var key = MonitoredHost.BuildKey(nodeId, host.Type, host.VmId);
				seenKeys.Add(key);

				var previous = storedByKey.GetValueOrDefault(key);

				// A guest with no usable address is still a known guest: it keeps its stored
				// address until retention expires, so a reboot does not drop its DNS record.
				var hasIp = !string.IsNullOrWhiteSpace(host.Ip);
				var ip = hasIp ? host.Ip : previous?.Ip;

				var merged = new MonitoredHost
				{
					Key = key,
					NodeId = nodeId,
					NodeName = nodeName,
					Type = host.Type,
					VmId = host.VmId,
					Hostname = host.Hostname,
					Ip = ip,
					FirstSeenAt = previous?.FirstSeenAt ?? now,
					LastSeenAt = hasIp ? now : previous?.LastSeenAt ?? now,
					Present = hasIp,
					Excluded = previous?.Excluded ?? false,
					Pinned = previous?.Pinned ?? false
				};

				upserts.Add(merged);
				events.AddRange(DeriveEvents(previous, merged, hasIp, now));
			}
		}

		var deletions = new List<string>();

		foreach (var host in stored)
		{
			if (seenKeys.Contains(host.Key)) continue;

			// A node that failed this cycle tells us nothing about its hosts. Expiring them would
			// turn a transient API outage into a mass DNS deletion.
			if (!polledNodeIds.Contains(host.NodeId)) continue;

			if (host.Pinned) continue;

			var expired = now - host.LastSeenAt > retention;

			if (expired)
			{
				deletions.Add(host.Key);
				if (host.Present || host.Ip is not null)
					events.Add(Event(host, IpEventKind.Disappeared, host.Ip, null, now));
				continue;
			}

			// Still inside the retention window: keep the record, but stop calling it present.
			if (host.Present)
			{
				upserts.Add(host with { Present = false });
				events.Add(Event(host, IpEventKind.Disappeared, host.Ip, null, now));
			}
		}

		return new DiffResult(upserts, deletions, events);
	}

	private static IEnumerable<IpEvent> DeriveEvents(MonitoredHost? previous, MonitoredHost current, bool hasIp, DateTime now)
	{
		if (previous is null)
		{
			if (hasIp) yield return Event(current, IpEventKind.Appeared, null, current.Ip, now);
			yield break;
		}

		if (!string.Equals(previous.Hostname, current.Hostname, StringComparison.OrdinalIgnoreCase))
			yield return Event(current, IpEventKind.Renamed, previous.Ip, current.Ip, now);

		if (!hasIp)
		{
			// Only report the loss once, on the transition.
			if (previous.Present) yield return Event(current, IpEventKind.Disappeared, previous.Ip, null, now);
			yield break;
		}

		if (previous.Ip is null)
		{
			yield return Event(current, IpEventKind.Appeared, null, current.Ip, now);
			yield break;
		}

		if (!string.Equals(previous.Ip, current.Ip, StringComparison.OrdinalIgnoreCase))
			yield return Event(current, IpEventKind.Changed, previous.Ip, current.Ip, now);
		else if (!previous.Present)
			// Same address, but the host had dropped out and is back.
			yield return Event(current, IpEventKind.Appeared, null, current.Ip, now);
	}

	private static IpEvent Event(MonitoredHost host, IpEventKind kind, string? previousIp, string? currentIp, DateTime now)
	{
		return new IpEvent
		{
			HostKey = host.Key,
			Hostname = host.Hostname,
			Type = host.Type,
			NodeName = host.NodeName,
			Kind = kind,
			PreviousIp = previousIp,
			CurrentIp = currentIp,
			At = now
		};
	}
}
