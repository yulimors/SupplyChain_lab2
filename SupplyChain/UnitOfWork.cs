using Npgsql;

public class UnitOfWork : IDisposable
{
    private readonly NpgsqlConnection _connection;
    private readonly NpgsqlTransaction _transaction;

    public SupplierRepository Suppliers { get; }
    public PurchaseOrderRepository PurchaseOrders { get; }
    public StockRepository Stock { get; }
    public SqlShipmentRepository Shipments { get; }

    public UnitOfWork(string connectionString)
    {
        _connection = new NpgsqlConnection(connectionString);
        _connection.Open();
        _transaction = _connection.BeginTransaction();

        Suppliers = new SupplierRepository(_connection, _transaction);
        PurchaseOrders = new PurchaseOrderRepository(_connection, _transaction);
        Stock = new StockRepository(_connection, _transaction);
        Shipments = new SqlShipmentRepository(_connection, _transaction);
    }

    public void Commit() => _transaction.Commit();

    public void Rollback() => _transaction.Rollback();

    public void Dispose()
    {
        _transaction.Dispose();
        _connection.Dispose();
    }
}

