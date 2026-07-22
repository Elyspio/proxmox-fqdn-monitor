using Elyspio.Utils.Telemetry.Technical.Helpers;
using Elyspio.Utils.Telemetry.Tracing.Elements;
using Microsoft.Extensions.Logging;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.Repositories;
using MongoDB.Bson;
using MongoDB.Driver;
using ProxmoxIpMonitor.Adapters.MongoDB.Mongo;

namespace ProxmoxIpMonitor.Adapters.MongoDB.Protection;

/// <summary>
///     Persists the Data Protection key ring in Mongo, so the encrypted API tokens travel with a
///     database backup and the deployment needs no volume of its own.
/// </summary>
public sealed class MongoXmlRepository(MongoContext context, ILogger<MongoXmlRepository> logger)
	: TracingRepository(logger), IXmlRepository
{
	private const string XmlField = "xml";
	private const string FriendlyNameField = "friendlyName";

	public IReadOnlyCollection<XElement> GetAllElements()
	{
		using var trace = LogRepository();

		var documents = context.DataProtectionKeys
			.Find(FilterDefinition<BsonDocument>.Empty)
			.ToList();

		return documents
			.Where(document => document.Contains(XmlField))
			.Select(document => XElement.Parse(document[XmlField].AsString))
			.ToList();
	}

	public void StoreElement(XElement element, string friendlyName)
	{
		// The element holds key material: only the friendly name goes through Log.F.
		using var trace = LogRepository($"{Log.F(friendlyName)}");

		ArgumentNullException.ThrowIfNull(element);

		var document = new BsonDocument
		{
			[FriendlyNameField] = friendlyName ?? string.Empty,
			[XmlField] = element.ToString(SaveOptions.DisableFormatting)
		};

		context.DataProtectionKeys.InsertOne(document);
	}
}
