using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using ProxmoxIpMonitor.Abstractions.Interfaces;
using ProxmoxIpMonitor.Abstractions.Models;

namespace ProxmoxIpMonitor.Adapters.MongoDB.Mongo;

/// <summary>
///     Creates the query indexes and the TTL indexes that bound the three journals.
///     TTL expiry is fixed when the index is created, so a change to
///     <see cref="AppSettings.JournalRetentionDays" /> requires dropping and recreating it —
///     handled here rather than left as a silent no-op.
/// </summary>
public sealed class MongoIndexInitializer(
	MongoContext context,
	ISettingsRepository settings,
	ILogger<MongoIndexInitializer> logger) : IHostedService
{
	private const string TtlIndexName = "ttl";

	public async Task StartAsync(CancellationToken cancellationToken)
	{
		try
		{
			var current = await settings.GetAsync(cancellationToken);
			var expiry = TimeSpan.FromDays(Math.Max(1, current.JournalRetentionDays));

			await context.Hosts.Indexes.CreateOneAsync(
				new CreateIndexModel<MonitoredHost>(Builders<MonitoredHost>.IndexKeys.Ascending(host => host.NodeId)),
				cancellationToken: cancellationToken);

			await context.IpEvents.Indexes.CreateOneAsync(
				new CreateIndexModel<IpEvent>(Builders<IpEvent>.IndexKeys.Ascending(evt => evt.HostKey).Descending(evt => evt.At)),
				cancellationToken: cancellationToken);

			await context.CollectionRuns.Indexes.CreateOneAsync(
				new CreateIndexModel<CollectionRun>(Builders<CollectionRun>.IndexKeys.Ascending(run => run.NodeId).Descending(run => run.StartedAt)),
				cancellationToken: cancellationToken);

			await EnsureTtlAsync(context.IpEvents, Builders<IpEvent>.IndexKeys.Ascending(evt => evt.At), expiry, cancellationToken);
			await EnsureTtlAsync(context.CollectionRuns, Builders<CollectionRun>.IndexKeys.Ascending(run => run.StartedAt), expiry, cancellationToken);
			await EnsureTtlAsync(context.DnsPushes, Builders<DnsPush>.IndexKeys.Ascending(push => push.At), expiry, cancellationToken);
		}
		catch (Exception e)
		{
			// A missing index degrades performance and retention; it must not stop the app from
			// starting, because the UI is also how the operator fixes a broken Mongo connection string.
			logger.LogError(e, "Failed to initialise MongoDB indexes");
		}
	}

	public Task StopAsync(CancellationToken cancellationToken)
	{
		return Task.CompletedTask;
	}

	private async Task EnsureTtlAsync<T>(IMongoCollection<T> collection, IndexKeysDefinition<T> keys, TimeSpan expiry, CancellationToken ct)
	{
		var existing = await (await collection.Indexes.ListAsync(ct)).ToListAsync(ct);
		var current = existing.FirstOrDefault(index => index.GetValue("name", "").AsString == TtlIndexName);

		if (current is not null)
		{
			var seconds = current.TryGetValue("expireAfterSeconds", out var value) ? value.ToDouble() : -1;
			if (Math.Abs(seconds - expiry.TotalSeconds) < 1) return;

			logger.LogInformation("Recreating TTL index on {Collection}: {Old}s -> {New}s", collection.CollectionNamespace.CollectionName, seconds, expiry.TotalSeconds);
			await collection.Indexes.DropOneAsync(TtlIndexName, ct);
		}

		await collection.Indexes.CreateOneAsync(
			new CreateIndexModel<T>(keys, new CreateIndexOptions { Name = TtlIndexName, ExpireAfter = expiry }),
			cancellationToken: ct);
	}
}
