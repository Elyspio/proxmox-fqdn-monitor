using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using ProxmoxIpMonitor.Abstractions.Models;

namespace ProxmoxIpMonitor.Adapters.MongoDB.Mongo;

/// <summary>
///     Reads a <see cref="Subnet" /> from either shape it may hold on disk. Settings documents written
///     before the subnet metadata existed store each subnet as a bare CIDR string; a plain string must
///     load as a CIDR-only subnet rather than throw. Current documents store <c>{ Cidr, Label? }</c>;
///     any other fields (e.g. a VlanId from an intermediate build) are ignored. Writing always produces
///     the document shape, so a document upgrades in place on the next save.
/// </summary>
public sealed class SubnetBsonSerializer : SerializerBase<Subnet>
{
	public override Subnet Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args)
	{
		var reader = context.Reader;

		switch (reader.GetCurrentBsonType())
		{
			case BsonType.String:
				return new Subnet { Cidr = reader.ReadString() };

			case BsonType.Document:
			{
				string cidr = "";
				string? label = null;

				reader.ReadStartDocument();
				while (reader.ReadBsonType() != BsonType.EndOfDocument)
				{
					// Accept both the PascalCase members the driver writes and camelCase, defensively.
					switch (reader.ReadName())
					{
						case "Cidr" or "cidr":
							cidr = reader.ReadString();
							break;
						case "Label" or "label":
							if (reader.GetCurrentBsonType() == BsonType.Null) reader.ReadNull();
							else label = reader.ReadString();
							break;
						default:
							reader.SkipValue();
							break;
					}
				}

				reader.ReadEndDocument();
				return new Subnet { Cidr = cidr, Label = label };
			}

			default:
				reader.SkipValue();
				return new Subnet { Cidr = "" };
		}
	}

	public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, Subnet value)
	{
		var writer = context.Writer;

		writer.WriteStartDocument();
		writer.WriteName("Cidr");
		writer.WriteString(value.Cidr);

		if (value.Label is { } label)
		{
			writer.WriteName("Label");
			writer.WriteString(label);
		}

		writer.WriteEndDocument();
	}
}
