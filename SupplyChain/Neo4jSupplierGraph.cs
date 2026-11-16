using Neo4j.Driver;
using Npgsql;

public class Neo4jSupplierGraph : IDisposable
{
    private readonly IDriver _driver;

    public Neo4jSupplierGraph(string uri, string user, string password)
    {
        _driver = GraphDatabase.Driver(uri, AuthTokens.Basic(user, password));
    }

    public async Task LoadFrom(string postgresConnection)
    {
        using var connection = new NpgsqlConnection(postgresConnection);
        connection.Open();

        await Query("MATCH (n) DETACH DELETE n");

        await Load(
            "UNWIND $rows AS row CREATE (:Supplier {id: row.id, name: row.name, country: row.country})",
            ReadRows(connection, "SELECT supplier_id AS id, name, COALESCE(country, '') AS country FROM vw_active_suppliers"));

        await Load(
            "UNWIND $rows AS row CREATE (:Product {id: row.id, sku: row.sku, name: row.name})",
            ReadRows(connection, "SELECT product_id AS id, sku, name FROM product WHERE NOT is_deleted"));

        await Load(
            "UNWIND $rows AS row CREATE (:Warehouse {id: row.id, name: row.name, city: row.city})",
            ReadRows(connection, "SELECT warehouse_id AS id, name, city FROM warehouse WHERE NOT is_deleted"));

        await Load("""
            UNWIND $rows AS row
            MATCH (s:Supplier {id: row.supplier}), (p:Product {id: row.product})
            CREATE (s)-[:SUPPLIES {price: row.price, leadTimeDays: row.lead_time}]->(p)
            """,
            ReadRows(connection, """
                SELECT supplier_id AS supplier, product_id AS product,
                       unit_price::float8 AS price, lead_time_days AS lead_time
                FROM supplier_product
                """));

        await Load("""
            UNWIND $rows AS row
            MATCH (s:Supplier {id: row.supplier}), (w:Warehouse {id: row.warehouse})
            CREATE (s)-[:DELIVERS_TO]->(w)
            """,
            ReadRows(connection, """
                SELECT DISTINCT supplier_id AS supplier, warehouse_id AS warehouse
                FROM purchase_order
                WHERE status <> 'cancelled'
                """));
    }

    public async Task<List<string>> ProductsOfSupplier(int supplierId)
    {
        var records = await Query("""
            MATCH (s:Supplier {id: $id})-[r:SUPPLIES]->(p:Product)
            RETURN p.name AS product, r.price AS price
            ORDER BY product
            """, new() { ["id"] = supplierId });

        return records.Select(r => $"{r["product"].As<string>()}: {r["price"].As<double>()} грн").ToList();
    }

    public async Task<List<string>> SuppliersOfProduct(int productId)
    {
        var records = await Query("""
            MATCH (s:Supplier)-[r:SUPPLIES]->(:Product {id: $id})
            RETURN s.name AS supplier, r.price AS price
            ORDER BY price
            """, new() { ["id"] = productId });

        return records.Select(r => $"{r["supplier"].As<string>()}: {r["price"].As<double>()} грн").ToList();
    }

    public async Task<List<string>> WarehousesOfSupplier(int supplierId)
    {
        var records = await Query("""
            MATCH (:Supplier {id: $id})-[:DELIVERS_TO]->(w:Warehouse)
            RETURN w.name AS warehouse
            ORDER BY warehouse
            """, new() { ["id"] = supplierId });

        return records.Select(r => r["warehouse"].As<string>()).ToList();
    }

    public async Task<List<string>> ProductsWithoutAlternative(int supplierId)
    {
        var records = await Query("""
            MATCH (s:Supplier {id: $id})-[:SUPPLIES]->(p:Product)
            WHERE NOT EXISTS {
                MATCH (other:Supplier)-[:SUPPLIES]->(p)
                WHERE other.id <> $id
            }
            RETURN p.name AS product
            ORDER BY product
            """, new() { ["id"] = supplierId });

        return records.Select(r => r["product"].As<string>()).ToList();
    }

    public void Dispose()
    {
        _driver.Dispose();
    }

    private async Task<IReadOnlyList<IRecord>> Query(string cypher, Dictionary<string, object>? parameters = null)
    {
        var result = await _driver
            .ExecutableQuery(cypher)
            .WithParameters(parameters ?? new Dictionary<string, object>())
            .ExecuteAsync();

        return result.Result;
    }

    private Task<IReadOnlyList<IRecord>> Load(string cypher, List<Dictionary<string, object>> rows)
    {
        return Query(cypher, new Dictionary<string, object> { ["rows"] = rows });
    }

    private static List<Dictionary<string, object>> ReadRows(NpgsqlConnection connection, string sql)
    {
        using var cmd = new NpgsqlCommand(sql, connection);
        using var reader = cmd.ExecuteReader();

        var rows = new List<Dictionary<string, object>>();
        while (reader.Read())
        {
            var row = new Dictionary<string, object>();
            for (int i = 0; i < reader.FieldCount; i++)
                row[reader.GetName(i)] = reader.GetValue(i);
            rows.Add(row);
        }
        return rows;
    }
}
