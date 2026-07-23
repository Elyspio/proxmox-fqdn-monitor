using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Bson.Serialization;
using ProxmoxIpMonitor.Abstractions.Models;
using ProxmoxIpMonitor.Adapters.MongoDB.Mongo;
using Xunit;

namespace ProxmoxIpMonitor.Adapters.Tests;

public class SubnetBsonSerializerTests
{
	private static readonly SubnetBsonSerializer Serializer = new();

	private static Subnet Deserialize(BsonValue stored)
	{
		// A subnet is stored as an array element, so wrap it in a document the reader can walk.
		var document = new BsonDocument("v", stored);
		using var reader = new BsonDocumentReader(document);
		var context = BsonDeserializationContext.CreateRoot(reader);
		reader.ReadStartDocument();
		reader.ReadName();
		var subnet = Serializer.Deserialize(context);
		reader.ReadEndDocument();
		return subnet;
	}

	private static BsonDocument Serialize(Subnet subnet)
	{
		var document = new BsonDocument();
		using var writer = new BsonDocumentWriter(document);
		var context = BsonSerializationContext.CreateRoot(writer);
		writer.WriteStartDocument();
		writer.WriteName("v");
		Serializer.Serialize(context, subnet);
		writer.WriteEndDocument();
		return document["v"].AsBsonDocument;
	}

	[Fact]
	public void Reads_a_legacy_cidr_string_as_a_cidr_only_subnet()
	{
		var subnet = Deserialize("10.0.0.0/8");

		Assert.Equal("10.0.0.0/8", subnet.Cidr);
		Assert.Null(subnet.Label);
	}

	[Fact]
	public void Reads_the_full_document_shape()
	{
		var stored = new BsonDocument { { "Cidr", "10.0.0.0/24" }, { "Label", "Services" } };

		var subnet = Deserialize(stored);

		Assert.Equal("10.0.0.0/24", subnet.Cidr);
		Assert.Equal("Services", subnet.Label);
	}

	[Fact]
	public void Reads_explicit_null_label()
	{
		var stored = new BsonDocument { { "Cidr", "10.0.1.0/24" }, { "Label", BsonNull.Value } };

		var subnet = Deserialize(stored);

		Assert.Equal("10.0.1.0/24", subnet.Cidr);
		Assert.Null(subnet.Label);
	}

	[Fact]
	public void Ignores_an_unknown_field_from_an_intermediate_build()
	{
		// A build between merges briefly wrote a VlanId; it must not break deserialization now.
		var stored = new BsonDocument { { "Cidr", "10.0.3.0/24" }, { "VlanId", 7 }, { "Label", "Legacy" } };

		var subnet = Deserialize(stored);

		Assert.Equal("10.0.3.0/24", subnet.Cidr);
		Assert.Equal("Legacy", subnet.Label);
	}

	[Fact]
	public void Writes_the_document_shape_and_omits_absent_label()
	{
		var document = Serialize(new Subnet { Cidr = "10.0.2.0/24" });

		Assert.Equal("10.0.2.0/24", document["Cidr"].AsString);
		Assert.False(document.Contains("Label"));
	}

	[Fact]
	public void Round_trips_a_named_subnet()
	{
		var original = new Subnet { Cidr = "10.1.0.0/24", Label = "Données" };

		var restored = Deserialize(Serialize(original));

		Assert.Equal(original, restored);
	}
}
