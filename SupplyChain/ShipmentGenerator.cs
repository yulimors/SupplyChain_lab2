public class ShipmentGenerator
{
    private const int EventsPerShipment = 10;

    private static readonly string[] Cities = { "Київ", "Львів", "Житомир", "Харків", "Одеса", "Дніпро" };
    private static readonly string[] DelayReasons = { "негода", "затор", "поломка транспорту", "митниця" };

    private static readonly (int Id, string Sku, string Name)[] Products =
    {
        (1, "SKU-MLK-001", "Молоко 2.5% 1 л"),
        (2, "SKU-CHS-001", "Сир кисломолочний"),
        (3, "SKU-YGT-001", "Йогурт")
    };

    private readonly Random _random;

    public ShipmentGenerator(int seed)
    {
        _random = new Random(seed);
    }

    public static int CarrierId(int number) => 1 + number % 5;

    public static string TrackingNumber(int number) => $"TRK-{number:D7}";

    public List<ShipmentDoc> CreateRange(int firstNumber, int count)
    {
        return Enumerable.Range(firstNumber, count).Select(Create).ToList();
    }

    public ShipmentDoc Create(int number)
    {
        int carrierId = CarrierId(number);
        var shippedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            .AddMinutes(_random.Next(0, 600_000));

        return new ShipmentDoc
        {
            SoId = number,
            Carrier = new CarrierInfo { Id = carrierId, Name = $"Перевізник {carrierId}", Transport = "road" },
            Warehouse = CreateWarehouse(number),
            TrackingNumber = TrackingNumber(number),
            Status = "in_transit",
            ShippedAt = shippedAt,
            FreightCost = 100 + number % 400,
            Items = CreateItems(number),
            Events = CreateEvents(shippedAt)
        };
    }

    private static WarehouseInfo CreateWarehouse(int number)
    {
        return number % 2 == 0
            ? new WarehouseInfo { Id = 1, Name = "Склад Київ", City = "Київ" }
            : new WarehouseInfo { Id = 2, Name = "Склад Львів", City = "Львів" };
    }

    private static List<ShipmentItemDoc> CreateItems(int number)
    {
        return Products.Select(p => new ShipmentItemDoc
        {
            SoItemId = (number - 1) * 3 + p.Id,
            ProductId = p.Id,
            Sku = p.Sku,
            Name = p.Name,
            Quantity = 10 * p.Id
        }).ToList();
    }

    private List<TrackingEventDoc> CreateEvents(DateTime shippedAt)
    {
        var events = new List<TrackingEventDoc>();
        var time = shippedAt;

        for (int i = 0; i < EventsPerShipment; i++)
        {
            string type = i == 0 ? "departed" : RandomType();
            events.Add(CreateEvent(type, time));
            time = time.AddHours(_random.Next(1, 12));
        }

        return events;
    }

    private string RandomType()
    {
        double roll = _random.NextDouble();

        if (roll < 0.01)
            return "damage";
        if (roll < 0.20)
            return "delay";
        return "departed";
    }

    private TrackingEventDoc CreateEvent(string type, DateTime time)
    {
        return new TrackingEventDoc
        {
            Ts = time,
            Type = type,
            Location = Cities[_random.Next(Cities.Length)],
            Details = CreateDetails(type)
        };
    }

    private Dictionary<string, string> CreateDetails(string type)
    {
        return type switch
        {
            "delay" => new()
            {
                ["reason"] = DelayReasons[_random.Next(DelayReasons.Length)],
                ["delayHours"] = _random.Next(1, 24).ToString()
            },
            "damage" => new()
            {
                ["description"] = "пошкоджена упаковка",
                ["severity"] = _random.Next(1, 4).ToString()
            },
            _ => new()
            {
                ["dock"] = "B" + _random.Next(1, 10)
            }
        };
    }
}

