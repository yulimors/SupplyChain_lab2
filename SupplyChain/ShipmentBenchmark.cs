using System.Diagnostics;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Driver;
using Npgsql;

public class ShipmentBenchmark
{
    private const int BatchSize = 5000;
    private const int WarmUpRuns = 50;
    private const int ReadRuns = 500;
    private const int InsertRuns = 200;
    private const int AggregationRuns = 5;

    private static readonly int[] Sizes = { 1_000, 10_000, 100_000 };

    private readonly string _postgresConnection;
    private readonly MongoShipmentRepository _mongo;

    public ShipmentBenchmark(string postgresConnection, string mongoConnection)
    {
        _postgresConnection = postgresConnection;
        var database = new MongoClient(mongoConnection).GetDatabase("logist_system");
        _mongo = new MongoShipmentRepository(database);
    }

    public void Run()
    {
        foreach (int size in Sizes)
        {
            Console.WriteLine();
            Console.WriteLine($"{size} відправлень, {size * 10} подій");

            PrepareSql(size + WarmUpRuns + InsertRuns);
            _mongo.Reset();

            Load(size);
            Verify(size);
            Measure(size);
        }
    }

    private void PrepareSql(int orderCount)
    {
        using var connection = new NpgsqlConnection(_postgresConnection);
        connection.Open();

        using var cmd = new NpgsqlCommand("""
            TRUNCATE tracking_event_attribute, tracking_event, shipment_item, shipment,
                     sales_order_item, sales_order, customer, carrier RESTART IDENTITY;

            INSERT INTO customer (name)
            SELECT 'Клієнт ' || g FROM generate_series(1, 100) g;

            INSERT INTO carrier (name, transport)
            SELECT 'Перевізник ' || g, 'road'::transport_type FROM generate_series(1, 5) g;

            INSERT INTO sales_order (so_id, customer_id, warehouse_id, shipping_address, created_by)
            SELECT g, 1 + g % 100, 1 + g % 2, 'Київ, вул. Тестова ' || g, 1
            FROM generate_series(1, @count) g;

            INSERT INTO sales_order_item (so_item_id, so_id, product_id, quantity, unit_price)
            SELECT (g - 1) * 3 + p, g, p, 10 * p, 25
            FROM generate_series(1, @count) g CROSS JOIN generate_series(1, 3) p;
            """, connection);
        cmd.Parameters.AddWithValue("count", orderCount);
        cmd.ExecuteNonQuery();
    }

    private void Load(int size)
    {
        var generator = new ShipmentGenerator(seed: 42);
        var sqlTime = new Stopwatch();
        var mongoTime = new Stopwatch();

        using var uow = new UnitOfWork(_postgresConnection);

        for (int first = 1; first <= size; first += BatchSize)
        {
            var batch = generator.CreateRange(first, Math.Min(BatchSize, size - first + 1));

            sqlTime.Start();
            uow.Shipments.BulkInsert(batch);
            sqlTime.Stop();

            mongoTime.Start();
            _mongo.InsertMany(batch);
            mongoTime.Stop();
        }

        sqlTime.Start();
        uow.Shipments.SyncSequences();
        uow.Commit();
        sqlTime.Stop();

        AnalyzeSql();

        Console.WriteLine(
            $"Масове завантаження: SQL {sqlTime.Elapsed.TotalSeconds:F1} с, MongoDB {mongoTime.Elapsed.TotalSeconds:F1} с");
    }

    private void AnalyzeSql()
    {
        using var connection = new NpgsqlConnection(_postgresConnection);
        connection.Open();

        using var cmd = new NpgsqlCommand("ANALYZE", connection);
        cmd.ExecuteNonQuery();
    }

    private void Verify(int size)
    {
        using var uow = new UnitOfWork(_postgresConnection);

        var sqlCounts = uow.Shipments.CountEventsByType();
        var mongoCounts = _mongo.CountEventsByType();

        bool same = sqlCounts.Count == mongoCounts.Count
            && sqlCounts.All(p => mongoCounts.TryGetValue(p.Key, out int count) && count == p.Value);

        Console.WriteLine(same
            ? "Перевірка: в обох базах однакова кількість подій кожного типу."
            : "Помилка: дані в базах відрізняються!");

        if (size == Sizes[0])
        {
            var sample = _mongo.GetByTracking(ShipmentGenerator.CarrierId(1), ShipmentGenerator.TrackingNumber(1));
            Console.WriteLine("Приклад документа в MongoDB:");
            Console.WriteLine(sample?.ToJson(new JsonWriterSettings { Indent = true }));
        }
    }

    private void Measure(int size)
    {
        var random = new Random(7);
        List<int> RandomShipments(int count) =>
            Enumerable.Range(0, count).Select(_ => random.Next(1, size + 1)).ToList();

        var warmUpReads = RandomShipments(WarmUpRuns);
        var reads = RandomShipments(ReadRuns);
        var warmUpAppends = RandomShipments(WarmUpRuns);
        var appends = RandomShipments(ReadRuns);

        var generator = new ShipmentGenerator(seed: 99);
        var newNumbers = Enumerable.Range(size + 1, WarmUpRuns + InsertRuns).ToList();
        var newShipments = newNumbers.ToDictionary(n => n, generator.Create);
        var warmUpInserts = newNumbers.Take(WarmUpRuns).ToList();
        var inserts = newNumbers.Skip(WarmUpRuns).ToList();

        var singleWarmUp = new List<int> { 0 };
        var aggregationRuns = Enumerable.Range(1, AggregationRuns).ToList();

        Console.WriteLine();
        Console.WriteLine($"{"Операція (середній час однієї, мс)",-52}{"SQL",10}{"MongoDB",10}");

        using var reader = new UnitOfWork(_postgresConnection);

        Compare("Прочитати відправлення з товарами і подіями", warmUpReads, reads,
            n => reader.Shipments.GetByTracking(ShipmentGenerator.CarrierId(n), ShipmentGenerator.TrackingNumber(n)),
            n => _mongo.GetByTracking(ShipmentGenerator.CarrierId(n), ShipmentGenerator.TrackingNumber(n)));

        Compare("Порахувати відправлення з пошкодженням", singleWarmUp, aggregationRuns,
            _ => reader.Shipments.CountShipmentsWithEventType("damage"),
            _ => _mongo.CountShipmentsWithEventType("damage"));

        Compare("Порахувати події кожного типу по всій базі", singleWarmUp, aggregationRuns,
            _ => reader.Shipments.CountEventsByType(),
            _ => _mongo.CountEventsByType());

        Compare("Додати подію в історію відправлення", warmUpAppends, appends,
            n =>
            {
                using var uow = new UnitOfWork(_postgresConnection);
                uow.Shipments.AddEvent(
                    ShipmentGenerator.CarrierId(n), ShipmentGenerator.TrackingNumber(n), NewEvent(), "in_transit");
                uow.Commit();
            },
            n => _mongo.AddEvent(
                ShipmentGenerator.CarrierId(n), ShipmentGenerator.TrackingNumber(n), NewEvent(), "in_transit"));

        Compare("Створити відправлення (3 товари, 10 подій)", warmUpInserts, inserts,
            n =>
            {
                using var uow = new UnitOfWork(_postgresConnection);
                uow.Shipments.Insert(newShipments[n]);
                uow.Commit();
            },
            n => _mongo.Insert(newShipments[n]));
    }

    private static TrackingEventDoc NewEvent()
    {
        return new TrackingEventDoc
        {
            Ts = DateTime.UtcNow,
            Type = "delay",
            Location = "Київ",
            Details = new() { ["reason"] = "затор", ["delayHours"] = "2" }
        };
    }

    private static void Compare(string name, List<int> warmUp, List<int> runs, Action<int> sql, Action<int> mongo)
    {
        double sqlMs = AverageMs(warmUp, runs, sql);
        double mongoMs = AverageMs(warmUp, runs, mongo);
        Console.WriteLine($"{name,-52}{sqlMs,10:F3}{mongoMs,10:F3}");
    }

    private static double AverageMs(List<int> warmUp, List<int> runs, Action<int> action)
    {
        foreach (int n in warmUp)
            action(n);

        var watch = Stopwatch.StartNew();
        foreach (int n in runs)
            action(n);
        watch.Stop();

        return watch.Elapsed.TotalMilliseconds / runs.Count;
    }
}
