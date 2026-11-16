Console.OutputEncoding = System.Text.Encoding.UTF8;

const string postgresConnection = "Host=localhost;Database=logist_system;Username=yulia;Password=pass";
const string mongoConnection = "mongodb://localhost:27017";
const string neo4jUri = "bolt://localhost:7687";

Console.WriteLine("1 - порівняння швидкодії SQL і MongoDB");
Console.WriteLine("2 - Neo4j: мережа постачальників");

switch (Console.ReadLine())
{
    case "1":
        new ShipmentBenchmark(postgresConnection, mongoConnection).Run();
        break;

    case "2":
        using (var graph = new Neo4jSupplierGraph(neo4jUri, "neo4j", "password123"))
        {
            await graph.LoadFrom(postgresConnection);

            Print("Товари постачальника 1", await graph.ProductsOfSupplier(1));
            Print("Постачальники товару 1 (від дешевшого)", await graph.SuppliersOfProduct(1));
            Print("Склади, які отримують товар від постачальника 1", await graph.WarehousesOfSupplier(1));
            Print("Товари без іншого постачальника, якщо постачальник 1 зупиниться", await graph.ProductsWithoutAlternative(1));
        }
        break;
}

static void Print(string title, List<string> lines)
{
    Console.WriteLine();
    Console.WriteLine(title + ":");
    foreach (var line in lines)
        Console.WriteLine("  " + line);
}
