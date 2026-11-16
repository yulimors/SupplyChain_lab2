using System.Globalization;
using Npgsql;

public class SqlShipmentRepository
{
    private readonly NpgsqlConnection _connection;
    private readonly NpgsqlTransaction _transaction;

    private int _nextShipmentId = 1;
    private long _nextEventId = 1;
    private long _nextAttributeId = 1;

    public SqlShipmentRepository(NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        _connection = connection;
        _transaction = transaction;
    }

    private NpgsqlCommand Command(string sql) => new NpgsqlCommand(sql, _connection, _transaction);

    public void Insert(ShipmentDoc shipment)
    {
        using var cmd = Command("""
            INSERT INTO shipment (so_id, carrier_id, warehouse_id, tracking_number, status, shipped_at, freight_cost)
            VALUES (@so, @carrier, @warehouse, @tracking, @status::shipment_status, @shipped, @freight)
            RETURNING shipment_id
            """);
        cmd.Parameters.AddWithValue("so", shipment.SoId);
        cmd.Parameters.AddWithValue("carrier", shipment.Carrier.Id);
        cmd.Parameters.AddWithValue("warehouse", shipment.Warehouse.Id);
        cmd.Parameters.AddWithValue("tracking", shipment.TrackingNumber);
        cmd.Parameters.AddWithValue("status", shipment.Status);
        cmd.Parameters.AddWithValue("shipped", (object?)shipment.ShippedAt ?? DBNull.Value);
        cmd.Parameters.AddWithValue("freight", (object?)shipment.FreightCost ?? DBNull.Value);
        int shipmentId = (int)cmd.ExecuteScalar()!;

        foreach (var item in shipment.Items)
            InsertItem(shipmentId, item);

        foreach (var trackingEvent in shipment.Events)
            InsertEvent(shipmentId, trackingEvent);
    }

    public bool AddEvent(int carrierId, string trackingNumber, TrackingEventDoc trackingEvent, string newStatus)
    {
        using var find = Command("SELECT shipment_id FROM shipment WHERE carrier_id = @carrier AND tracking_number = @tracking");
        find.Parameters.AddWithValue("carrier", carrierId);
        find.Parameters.AddWithValue("tracking", trackingNumber);
        object? found = find.ExecuteScalar();

        if (found == null)
            return false;

        int shipmentId = (int)found;
        InsertEvent(shipmentId, trackingEvent);

        using var update = Command("UPDATE shipment SET status = @status::shipment_status WHERE shipment_id = @shipment");
        update.Parameters.AddWithValue("status", newStatus);
        update.Parameters.AddWithValue("shipment", shipmentId);
        update.ExecuteNonQuery();

        return true;
    }

    public ShipmentDoc? GetByTracking(int carrierId, string trackingNumber)
    {
        var shipment = ReadShipment(carrierId, trackingNumber, out int shipmentId);
        if (shipment == null)
            return null;

        ReadItems(shipmentId, shipment);
        ReadEvents(shipmentId, shipment);
        return shipment;
    }

    public int CountShipmentsWithEventType(string eventType)
    {
        using var cmd = Command("SELECT count(DISTINCT shipment_id) FROM tracking_event WHERE event_type = @type");
        cmd.Parameters.AddWithValue("type", eventType);
        return (int)(long)cmd.ExecuteScalar()!;
    }

    public Dictionary<string, int> CountEventsByType()
    {
        using var cmd = Command("SELECT event_type, count(*) FROM tracking_event GROUP BY event_type");
        using var reader = cmd.ExecuteReader();

        var result = new Dictionary<string, int>();
        while (reader.Read())
            result[reader.GetString(0)] = (int)reader.GetInt64(1);
        return result;
    }

    public void BulkInsert(List<ShipmentDoc> shipments)
    {
        CopyShipments(shipments);
        CopyItems(shipments);
        CopyEvents(shipments);
        CopyAttributes(shipments);

        _nextShipmentId += shipments.Count;
        _nextEventId += shipments.Sum(s => s.Events.Count);
        _nextAttributeId += shipments.Sum(s => s.Events.Sum(e => e.Details.Count));
    }

    public void SyncSequences()
    {
        var columns = new[]
        {
            ("shipment", "shipment_id"),
            ("tracking_event", "event_id"),
            ("tracking_event_attribute", "attribute_id")
        };

        foreach (var (table, column) in columns)
        {
            using var cmd = Command(
                $"SELECT setval(pg_get_serial_sequence('{table}', '{column}'), (SELECT max({column}) FROM {table}))");
            cmd.ExecuteNonQuery();
        }
    }

    private void InsertItem(int shipmentId, ShipmentItemDoc item)
    {
        using var cmd = Command(
            "INSERT INTO shipment_item (shipment_id, so_item_id, quantity) VALUES (@shipment, @item, @quantity)");
        cmd.Parameters.AddWithValue("shipment", shipmentId);
        cmd.Parameters.AddWithValue("item", item.SoItemId);
        cmd.Parameters.AddWithValue("quantity", item.Quantity);
        cmd.ExecuteNonQuery();
    }

    private void InsertEvent(int shipmentId, TrackingEventDoc trackingEvent)
    {
        using var cmd = Command("""
            INSERT INTO tracking_event (shipment_id, event_type, location, occurred_at)
            VALUES (@shipment, @type, @location, @occurred)
            RETURNING event_id
            """);
        cmd.Parameters.AddWithValue("shipment", shipmentId);
        cmd.Parameters.AddWithValue("type", trackingEvent.Type);
        cmd.Parameters.AddWithValue("location", (object?)trackingEvent.Location ?? DBNull.Value);
        cmd.Parameters.AddWithValue("occurred", trackingEvent.Ts);
        long eventId = (long)cmd.ExecuteScalar()!;

        foreach (var (name, value) in trackingEvent.Details)
        {
            using var attribute = Command(
                "INSERT INTO tracking_event_attribute (event_id, name, value) VALUES (@event, @name, @value)");
            attribute.Parameters.AddWithValue("event", eventId);
            attribute.Parameters.AddWithValue("name", name);
            attribute.Parameters.AddWithValue("value", value);
            attribute.ExecuteNonQuery();
        }
    }

    private ShipmentDoc? ReadShipment(int carrierId, string trackingNumber, out int shipmentId)
    {
        using var cmd = Command("""
            SELECT s.shipment_id, s.so_id, s.status::text, s.shipped_at, s.delivered_at, s.freight_cost,
                   c.carrier_id, c.name, c.transport::text,
                   w.warehouse_id, w.name, w.city
            FROM shipment s
            JOIN carrier   c ON c.carrier_id   = s.carrier_id
            JOIN warehouse w ON w.warehouse_id = s.warehouse_id
            WHERE s.carrier_id = @carrier AND s.tracking_number = @tracking
            """);
        cmd.Parameters.AddWithValue("carrier", carrierId);
        cmd.Parameters.AddWithValue("tracking", trackingNumber);
        using var reader = cmd.ExecuteReader();

        shipmentId = 0;
        if (!reader.Read())
            return null;

        shipmentId = reader.GetInt32(0);
        return new ShipmentDoc
        {
            SoId = reader.GetInt32(1),
            TrackingNumber = trackingNumber,
            Status = reader.GetString(2),
            ShippedAt = reader.IsDBNull(3) ? null : reader.GetDateTime(3),
            DeliveredAt = reader.IsDBNull(4) ? null : reader.GetDateTime(4),
            FreightCost = reader.IsDBNull(5) ? null : reader.GetDecimal(5),
            Carrier = new CarrierInfo
            {
                Id = reader.GetInt32(6),
                Name = reader.GetString(7),
                Transport = reader.GetString(8)
            },
            Warehouse = new WarehouseInfo
            {
                Id = reader.GetInt32(9),
                Name = reader.GetString(10),
                City = reader.GetString(11)
            }
        };
    }

    private void ReadItems(int shipmentId, ShipmentDoc shipment)
    {
        using var cmd = Command("""
            SELECT i.so_item_id, p.product_id, p.sku, p.name, i.quantity
            FROM shipment_item i
            JOIN sales_order_item o ON o.so_item_id = i.so_item_id
            JOIN product p          ON p.product_id = o.product_id
            WHERE i.shipment_id = @shipment
            ORDER BY i.so_item_id
            """);
        cmd.Parameters.AddWithValue("shipment", shipmentId);
        using var reader = cmd.ExecuteReader();

        while (reader.Read())
        {
            shipment.Items.Add(new ShipmentItemDoc
            {
                SoItemId = reader.GetInt32(0),
                ProductId = reader.GetInt32(1),
                Sku = reader.GetString(2),
                Name = reader.GetString(3),
                Quantity = reader.GetInt32(4)
            });
        }
    }

    private void ReadEvents(int shipmentId, ShipmentDoc shipment)
    {
        using var cmd = Command("""
            SELECT e.event_id, e.occurred_at, e.event_type, e.location, a.name, a.value
            FROM tracking_event e
            LEFT JOIN tracking_event_attribute a ON a.event_id = e.event_id
            WHERE e.shipment_id = @shipment
            ORDER BY e.occurred_at, e.event_id
            """);
        cmd.Parameters.AddWithValue("shipment", shipmentId);
        using var reader = cmd.ExecuteReader();

        TrackingEventDoc? current = null;
        long currentId = 0;

        while (reader.Read())
        {
            long eventId = reader.GetInt64(0);

            if (current == null || eventId != currentId)
            {
                current = new TrackingEventDoc
                {
                    Ts = reader.GetDateTime(1),
                    Type = reader.GetString(2),
                    Location = reader.IsDBNull(3) ? null : reader.GetString(3)
                };
                currentId = eventId;
                shipment.Events.Add(current);
            }

            if (!reader.IsDBNull(4))
                current.Details[reader.GetString(4)] = reader.GetString(5);
        }
    }

    private void CopyShipments(List<ShipmentDoc> shipments)
    {
        using var writer = _connection.BeginTextImport("""
            COPY shipment (shipment_id, so_id, carrier_id, warehouse_id, tracking_number, status, shipped_at, freight_cost)
            FROM STDIN
            """);

        int shipmentId = _nextShipmentId;
        foreach (var s in shipments)
        {
            writer.WriteLine(string.Join('\t',
                shipmentId++, s.SoId, s.Carrier.Id, s.Warehouse.Id, s.TrackingNumber, s.Status,
                Text(s.ShippedAt), Text(s.FreightCost)));
        }
    }

    private void CopyItems(List<ShipmentDoc> shipments)
    {
        using var writer = _connection.BeginTextImport(
            "COPY shipment_item (shipment_id, so_item_id, quantity) FROM STDIN");

        int shipmentId = _nextShipmentId;
        foreach (var s in shipments)
        {
            foreach (var item in s.Items)
                writer.WriteLine(string.Join('\t', shipmentId, item.SoItemId, item.Quantity));
            shipmentId++;
        }
    }

    private void CopyEvents(List<ShipmentDoc> shipments)
    {
        using var writer = _connection.BeginTextImport(
            "COPY tracking_event (event_id, shipment_id, event_type, location, occurred_at) FROM STDIN");

        long eventId = _nextEventId;
        int shipmentId = _nextShipmentId;
        foreach (var s in shipments)
        {
            foreach (var e in s.Events)
                writer.WriteLine(string.Join('\t', eventId++, shipmentId, e.Type, e.Location ?? "\\N", Text(e.Ts)));
            shipmentId++;
        }
    }

    private void CopyAttributes(List<ShipmentDoc> shipments)
    {
        using var writer = _connection.BeginTextImport(
            "COPY tracking_event_attribute (attribute_id, event_id, name, value) FROM STDIN");

        long eventId = _nextEventId;
        long attributeId = _nextAttributeId;
        foreach (var s in shipments)
        {
            foreach (var e in s.Events)
            {
                foreach (var (name, value) in e.Details)
                    writer.WriteLine(string.Join('\t', attributeId++, eventId, name, value));
                eventId++;
            }
        }
    }

    private static string Text(DateTime? value) => value?.ToString("O") ?? "\\N";

    private static string Text(decimal? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "\\N";
}

