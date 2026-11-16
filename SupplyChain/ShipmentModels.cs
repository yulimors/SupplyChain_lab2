using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

public class CarrierInfo
{
    [BsonElement("id")]
    public int Id { get; set; }

    [BsonElement("name")]
    public string Name { get; set; } = "";

    [BsonElement("transport")]
    public string? Transport { get; set; }
}

public class WarehouseInfo
{
    [BsonElement("id")]
    public int Id { get; set; }

    [BsonElement("name")]
    public string Name { get; set; } = "";

    [BsonElement("city")]
    public string? City { get; set; }
}

public class ShipmentItemDoc
{
    [BsonElement("soItemId")]
    public int SoItemId { get; set; }

    [BsonElement("productId")]
    public int ProductId { get; set; }

    [BsonElement("sku")]
    public string Sku { get; set; } = "";

    [BsonElement("name")]
    public string Name { get; set; } = "";

    [BsonElement("quantity")]
    public int Quantity { get; set; }
}

public class TrackingEventDoc
{
    [BsonElement("ts")]
    public DateTime Ts { get; set; }

    [BsonElement("type")]
    public string Type { get; set; } = "";

    [BsonElement("location")]
    public string? Location { get; set; }

    [BsonElement("details")]
    public Dictionary<string, string> Details { get; set; } = new();
}

[BsonIgnoreExtraElements]
public class ShipmentDoc
{
    [BsonId]
    public ObjectId Id { get; set; }

    [BsonElement("soId")]
    public int SoId { get; set; }

    [BsonElement("carrier")]
    public CarrierInfo Carrier { get; set; } = new();

    [BsonElement("warehouse")]
    public WarehouseInfo Warehouse { get; set; } = new();

    [BsonElement("trackingNumber")]
    public string TrackingNumber { get; set; } = "";

    [BsonElement("status")]
    public string Status { get; set; } = "planned";

    [BsonElement("shippedAt")]
    public DateTime? ShippedAt { get; set; }

    [BsonElement("deliveredAt")]
    public DateTime? DeliveredAt { get; set; }

    [BsonElement("freightCost")]
    [BsonRepresentation(BsonType.Decimal128)]
    public decimal? FreightCost { get; set; }

    [BsonElement("items")]
    public List<ShipmentItemDoc> Items { get; set; } = new();

    [BsonElement("events")]
    public List<TrackingEventDoc> Events { get; set; } = new();
}

