using MongoDB.Bson;
using MongoDB.Driver;

public class MongoShipmentRepository
{
    private readonly IMongoDatabase _database;
    private readonly IMongoCollection<ShipmentDoc> _shipments;

    public MongoShipmentRepository(IMongoDatabase database)
    {
        _database = database;
        _shipments = database.GetCollection<ShipmentDoc>("shipments");
    }

    public void Reset()
    {
        _database.DropCollection("shipments");

        var uniqueTracking = new CreateIndexModel<ShipmentDoc>(
            Builders<ShipmentDoc>.IndexKeys.Ascending("carrier.id").Ascending("trackingNumber"),
            new CreateIndexOptions { Unique = true });

        var eventType = new CreateIndexModel<ShipmentDoc>(
            Builders<ShipmentDoc>.IndexKeys.Ascending("events.type"));

        _shipments.Indexes.CreateMany(new[] { uniqueTracking, eventType });
    }

    public void Insert(ShipmentDoc shipment)
    {
        _shipments.InsertOne(shipment);
    }

    public void InsertMany(List<ShipmentDoc> shipments)
    {
        _shipments.InsertMany(shipments);
    }

    public ShipmentDoc? GetByTracking(int carrierId, string trackingNumber)
    {
        return _shipments
            .Find(s => s.Carrier.Id == carrierId && s.TrackingNumber == trackingNumber)
            .FirstOrDefault();
    }

    public bool AddEvent(int carrierId, string trackingNumber, TrackingEventDoc trackingEvent, string newStatus)
    {
        var update = Builders<ShipmentDoc>.Update
            .Push(s => s.Events, trackingEvent)
            .Set(s => s.Status, newStatus);

        var result = _shipments.UpdateOne(
            s => s.Carrier.Id == carrierId && s.TrackingNumber == trackingNumber,
            update);

        return result.MatchedCount == 1;
    }

    public int CountShipmentsWithEventType(string eventType)
    {
        var filter = Builders<ShipmentDoc>.Filter.ElemMatch(s => s.Events, e => e.Type == eventType);
        return (int)_shipments.CountDocuments(filter);
    }

    public Dictionary<string, int> CountEventsByType()
    {
        var stages = new[]
        {
            new BsonDocument("$unwind", "$events"),
            new BsonDocument("$group", new BsonDocument
            {
                { "_id", "$events.type" },
                { "count", new BsonDocument("$sum", 1) }
            })
        };

        var pipeline = PipelineDefinition<ShipmentDoc, BsonDocument>.Create(stages);

        return _shipments
            .Aggregate(pipeline)
            .ToList()
            .ToDictionary(d => d["_id"].AsString, d => d["count"].ToInt32());
    }
}
