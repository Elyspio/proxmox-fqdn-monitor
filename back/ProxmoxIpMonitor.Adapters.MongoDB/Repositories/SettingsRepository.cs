using Elyspio.Utils.Telemetry.Technical.Helpers;
using Elyspio.Utils.Telemetry.Tracing.Elements;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using ProxmoxIpMonitor.Abstractions.Interfaces;
using ProxmoxIpMonitor.Abstractions.Models;
using ProxmoxIpMonitor.Adapters.MongoDB.Mongo;

namespace ProxmoxIpMonitor.Adapters.MongoDB.Repositories;

/// <summary>
///     Single-document settings store. The document is created on first read so a fresh
///     deployment boots into a usable, empty state instead of failing on a missing config.
/// </summary>
public sealed class SettingsRepository(MongoContext context, ILogger<SettingsRepository> logger) : TracingRepository(logger), ISettingsRepository
{
	public async Task<AppSettings> GetAsync(CancellationToken ct = default)
	{
		using var trace = LogRepository();

		var filter = Builders<AppSettings>.Filter.Eq(settings => settings.Id, AppSettings.SingletonId);
		var stored = await context.Settings.Find(filter).FirstOrDefaultAsync(ct);
		if (stored is not null) return stored;

		var defaults = new AppSettings();
		await context.Settings.ReplaceOneAsync(filter, defaults, new ReplaceOptions { IsUpsert = true }, ct);
		return defaults;
	}

	public async Task<AppSettings> SaveAsync(AppSettings settings, CancellationToken ct = default)
	{
		using var trace = LogRepository();

		var toStore = settings with { Id = AppSettings.SingletonId };
		var filter = Builders<AppSettings>.Filter.Eq(s => s.Id, AppSettings.SingletonId);
		await context.Settings.ReplaceOneAsync(filter, toStore, new ReplaceOptions { IsUpsert = true }, ct);
		return toStore;
	}
}
