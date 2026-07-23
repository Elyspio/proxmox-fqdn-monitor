using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Conventions;
using MongoDB.Bson.Serialization.IdGenerators;
using MongoDB.Bson.Serialization.Serializers;
using ProxmoxIpMonitor.Abstractions.Models;
// Unqualified: this project's own namespace ends in ".MongoDB", so "MongoDB.Bson.Serialization
// .Serializers.StringSerializer" would bind relative to it and fail to resolve.

namespace ProxmoxIpMonitor.Adapters.MongoDB.Mongo;

/// <summary>
///     Class maps for the domain records. Registered once, before any collection is resolved.
/// </summary>
public static class MongoMappings
{
	private static bool _registered;

	public static void Register()
	{
		if (_registered) return;
		_registered = true;

		// Enums as strings: the journals are meant to be readable straight from the shell,
		// and an integer that shifts when a member is inserted is a silent data corruption.
		ConventionRegistry.Register(
			"proxmox-ip-monitor",
			new ConventionPack
			{
				new EnumRepresentationConvention(BsonType.String),
				new IgnoreExtraElementsConvention(true)
			},
			_ => true);

		// Custom: subnets predate their VLAN metadata, so a legacy CIDR string must still load.
		BsonSerializer.TryRegisterSerializer(new SubnetBsonSerializer());

		BsonClassMap.TryRegisterClassMap<AppSettings>(map =>
		{
			map.AutoMap();
			map.MapIdMember(settings => settings.Id);
		});

		BsonClassMap.TryRegisterClassMap<TechnitiumSettings>(map => map.AutoMap());

		BsonClassMap.TryRegisterClassMap<PveNode>(map =>
		{
			map.AutoMap();
			map.MapIdMember(node => node.Id)
				.SetSerializer(new StringSerializer(BsonType.ObjectId))
				.SetIdGenerator(StringObjectIdGenerator.Instance);
		});

		// The composite key is the document id: it makes "one row per guest" a storage guarantee
		// rather than something the collector has to remember to enforce.
		BsonClassMap.TryRegisterClassMap<MonitoredHost>(map =>
		{
			map.AutoMap();
			map.MapIdMember(host => host.Key);
		});

		BsonClassMap.TryRegisterClassMap<IpEvent>(map =>
		{
			map.AutoMap();
			map.MapIdMember(evt => evt.Id)
				.SetSerializer(new StringSerializer(BsonType.ObjectId))
				.SetIdGenerator(StringObjectIdGenerator.Instance);
		});

		BsonClassMap.TryRegisterClassMap<CollectionRun>(map =>
		{
			map.AutoMap();
			map.MapIdMember(run => run.Id)
				.SetSerializer(new StringSerializer(BsonType.ObjectId))
				.SetIdGenerator(StringObjectIdGenerator.Instance);
		});

		BsonClassMap.TryRegisterClassMap<HostIssue>(map => map.AutoMap());

		BsonClassMap.TryRegisterClassMap<DnsPush>(map =>
		{
			map.AutoMap();
			map.MapIdMember(push => push.Id)
				.SetSerializer(new StringSerializer(BsonType.ObjectId))
				.SetIdGenerator(StringObjectIdGenerator.Instance);
		});

		BsonClassMap.TryRegisterClassMap<DnsRecordOutcome>(map => map.AutoMap());
	}
}
