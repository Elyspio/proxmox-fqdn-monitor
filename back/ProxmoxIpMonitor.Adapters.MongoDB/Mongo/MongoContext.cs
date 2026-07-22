using MongoDB.Bson;
using MongoDB.Driver;
using ProxmoxIpMonitor.Abstractions.Models;

namespace ProxmoxIpMonitor.Adapters.MongoDB.Mongo;

/// <summary>Named access to the collections, so collection names live in exactly one place.</summary>
public sealed class MongoContext(IMongoDatabase database)
{
	public IMongoDatabase Database { get; } = database;

	public IMongoCollection<AppSettings> Settings => Database.GetCollection<AppSettings>("settings");

	public IMongoCollection<PveNode> Nodes => Database.GetCollection<PveNode>("nodes");

	public IMongoCollection<MonitoredHost> Hosts => Database.GetCollection<MonitoredHost>("hosts");

	public IMongoCollection<IpEvent> IpEvents => Database.GetCollection<IpEvent>("ipEvents");

	public IMongoCollection<CollectionRun> CollectionRuns => Database.GetCollection<CollectionRun>("collectionRuns");

	public IMongoCollection<DnsPush> DnsPushes => Database.GetCollection<DnsPush>("dnsPushes");

	public IMongoCollection<BsonDocument> DataProtectionKeys => Database.GetCollection<BsonDocument>("dataProtectionKeys");
}
