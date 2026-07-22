using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using ProxmoxIpMonitor.Abstractions.Interfaces;
using ProxmoxIpMonitor.Adapters.MongoDB.Mongo;
using ProxmoxIpMonitor.Adapters.MongoDB.Protection;
using ProxmoxIpMonitor.Adapters.MongoDB.Repositories;

namespace ProxmoxIpMonitor.Adapters.MongoDB.Injections;

public static class DbModule
{
	public static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration config)
	{
		MongoMappings.Register();

		var connectionString = config.GetConnectionString("MongoDB") ?? "mongodb://localhost:27017";
		var url = MongoUrl.Create(connectionString);
		var databaseName = !string.IsNullOrWhiteSpace(url.DatabaseName)
			? url.DatabaseName
			: config["Mongo:Database"] ?? "proxmox-ip-monitor";

		services.AddSingleton<IMongoClient>(_ => new MongoClient(connectionString));
		services.AddSingleton(sp => sp.GetRequiredService<IMongoClient>().GetDatabase(databaseName));
		services.AddSingleton<MongoContext>();

		services.AddSingleton<ISettingsRepository, SettingsRepository>();
		services.AddSingleton<INodeRepository, NodeRepository>();
		services.AddSingleton<IHostRepository, HostRepository>();
		services.AddSingleton<IIpEventRepository, IpEventRepository>();
		services.AddSingleton<ICollectionRunRepository, CollectionRunRepository>();
		services.AddSingleton<IDnsPushRepository, DnsPushRepository>();

		services.AddHostedService<MongoIndexInitializer>();

		services.AddTokenProtection(config);

		return services;
	}

	/// <summary>
	///     Wires Data Protection so the key ring lives in Mongo, wrapped with the configured master key.
	///     Both halves matter: Mongo makes the ring survive a pod restart, and the master key makes a
	///     database dump useless on its own.
	/// </summary>
	private static IServiceCollection AddTokenProtection(this IServiceCollection services, IConfiguration config)
	{
		var configured = config[DataProtectionMasterKey.ConfigurationKey];
		services.AddSingleton(_ => DataProtectionMasterKey.Parse(configured));

		services.AddSingleton<MongoXmlRepository>();
		services.AddSingleton<AesGcmXmlEncryptor>();

		services.AddDataProtection()
			// Pinned so a rename of the entry assembly cannot silently orphan the existing ring.
			.SetApplicationName("proxmox-ip-monitor");

		services.AddOptions<KeyManagementOptions>().Configure<IServiceProvider>((options, sp) =>
		{
			options.XmlRepository = sp.GetRequiredService<MongoXmlRepository>();
			options.XmlEncryptor = sp.GetRequiredService<AesGcmXmlEncryptor>();
		});

		services.AddSingleton<ISecretProtector, SecretProtector>();

		return services;
	}
}
