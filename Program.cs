using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Odbc;
using System.IO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Npgsql;

// Build configuration from appsettings.json and environment variables
var baseDir = AppContext.BaseDirectory;
var configuration = new ConfigurationBuilder()
    .SetBasePath(baseDir)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddEnvironmentVariables()
    .Build();

var as400Settings = configuration.GetSection("AS400").Get<AS400Settings>()
    ?? throw new InvalidOperationException("Missing AS400 configuration.");
var pgSettings = configuration.GetSection("Postgres").Get<PostgresSettings>()
    ?? throw new InvalidOperationException("Missing Postgres configuration");

DataTable allRows = new DataTable();

try
{
    Console.WriteLine(">>> Connecting to AS400...");

    using (var db = new AS400Helper(as400Settings.Host, as400Settings.User, as400Settings.Password, as400Settings.Library))
    {
        db.Connect();

        string lib = as400Settings.Library;

        // ── CUSTOMERS CRUD ──────────────────────────

        // CREATE: Insert new customers
        Console.WriteLine("\n>>> Inserting Customers...");
        InsertCustomer(db, lib, "Ravi", "Sharma", "ravi.sharma@email.com", "9876543210", "Kolkata");
        InsertCustomer(db, lib, "Priya", "Mehta", "priya.mehta@email.com", "9845001234", "Mumbai");
        InsertCustomer(db, lib, "Arjun", "Das", "arjun.das@email.com", "9831009988", "Delhi");
        InsertCustomer(db, lib, "Sneha", "Kapoor", "sneha.kapoor@email.com", "9900112233", "Bengaluru");

        // READ: Select all customers
        Console.WriteLine("\n>>> All Customers:");
        var customers = GetAllCustomers(db, lib);
        PrintTable(customers);

        var customersIds = customers.AsEnumerable()
                   .Select(row => row["CUSTOMER_ID"].ToString())
                   .ToList();

        // READ: Select customer by ID
        Console.WriteLine("\n>>> Get Customer by ID = 1:");
        var customer = GetCustomerById(db, lib, Convert.ToInt32(customersIds[0]));
        PrintTable(customer);

        // UPDATE: Update customer city
        Console.WriteLine("\n>>> Updating Customer ID = 1 city to 'Chennai'...");
        UpdateCustomerCity(db, lib, Convert.ToInt32(customersIds[0]), "Chennai");

        // READ: Verify update
        Console.WriteLine("\n>>> After Update - Customer ID = 1:");
        PrintTable(GetCustomerById(db, lib, Convert.ToInt32(customersIds[0])));

        // ── ORDERS CRUD ─────────────────────────────

        // CREATE: Insert new orders
        Console.WriteLine("\n>>> Inserting Orders...");
        InsertOrder(db, lib, Convert.ToInt32(customersIds[0]), 1500.00m, "COMPLETED", "Laptop bag and accessories");
        InsertOrder(db, lib, Convert.ToInt32(customersIds[0]), 350.75m, "PENDING", "Books order");
        InsertOrder(db, lib, Convert.ToInt32(customersIds[1]), 8999.00m, "COMPLETED", "Smartphone purchase");
        InsertOrder(db, lib, Convert.ToInt32(customersIds[2]), 450.00m, "CANCELLED", "Headphones");
        InsertOrder(db, lib, Convert.ToInt32(customersIds[3]), 2200.50m, "PENDING", "Office chair");

        // READ: Select all orders
        Console.WriteLine("\n>>> All Orders:");
        var orders = GetAllOrders(db, lib);
        PrintTable(orders);
        var orderIds = orders.AsEnumerable()
                     .Select(row => row["ORDER_ID"].ToString())
                     .ToList();
        // READ: Select orders by Customer ID
        Console.WriteLine("\n>>> Orders for Customer ID = 1:");
        var customerOrders = GetOrdersByCustomerId(db, lib, Convert.ToInt32(orderIds[0]));
        PrintTable(customerOrders);

        // UPDATE: Update order status
        Console.WriteLine("\n>>> Updating Order ID = 2 status to 'COMPLETED'...");
        UpdateOrderStatus(db, lib, Convert.ToInt32(orderIds[1]), "COMPLETED");

        // READ: Verify update
        Console.WriteLine("\n>>> All Orders after status update:");
        PrintTable(GetAllOrders(db, lib));

        // READ: Join Customers + Orders
        Console.WriteLine("\n>>> Customers with their Orders (JOIN):");
        var joined = GetCustomersWithOrders(db, lib);
        PrintTable(joined);

        // DELETE: Delete order by ID
        Console.WriteLine("\n>>> Deleting Order ID = 4...");
        DeleteOrderById(db, lib, Convert.ToInt32(orderIds[3]));

        // DELETE: Delete customer by ID
        Console.WriteLine("\n>>> Deleting Customer ID = 3 (cascades orders)...");
        DeleteCustomerById(db, lib, Convert.ToInt32(orderIds[3]));

        // READ: Final state
        Console.WriteLine("\n>>> Final Customers:");
        PrintTable(GetAllCustomers(db, lib));

        Console.WriteLine("\n>>> Final Orders:");
        PrintTable(GetAllOrders(db, lib));
    }
}
catch (OdbcException ex)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"[AS400 ERROR] {ex.Message}");
    Console.ResetColor();
}
catch (NpgsqlException ex)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"[POSTGRES ERROR] {ex.Message}");
    Console.ResetColor();
}
catch (Exception ex)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"[ERROR] {ex.Message}");
    Console.ResetColor();
}

// Wait for key only when running interactively (prevents ReadKey exception in containers)
Console.WriteLine("\nPress any key to exit...");
if (Environment.UserInteractive)
{
    try
    {
        Console.ReadKey(true);
    }
    catch
    {
        // ignore
    }
}
else
{
    Console.WriteLine("Non-interactive environment detected; exiting.");
}


// ─────────────────────────────────────────
//  CUSTOMERS CRUD METHODS
// ─────────────────────────────────────────

static void InsertCustomer(AS400Helper db, string lib, string firstName, string lastName,
    string email, string phone, string city)
{
    string sql = $@"INSERT INTO {lib}.CUSTOMERS (FIRST_NAME, LAST_NAME, EMAIL, PHONE, CITY)
                    VALUES (?, ?, ?, ?, ?)";
    int affected = db.ExecuteNonQuery(sql, new List<object> { firstName, lastName, email, phone, city });
    Console.WriteLine($"  ✓ Inserted customer: {firstName} {lastName} ({affected} row)");
}

static DataTable GetAllCustomers(AS400Helper db, string lib)
{
    string sql = $"SELECT * FROM {lib}.CUSTOMERS ORDER BY CUSTOMER_ID";
    return db.ExecuteQuery(sql);
}

static DataTable GetCustomerById(AS400Helper db, string lib, int customerId)
{
    string sql = $"SELECT * FROM {lib}.CUSTOMERS WHERE CUSTOMER_ID = ?";
    return db.ExecuteQuery(sql, new List<object> { customerId });
}

static void UpdateCustomerCity(AS400Helper db, string lib, int customerId, string newCity)
{
    string sql = $"UPDATE {lib}.CUSTOMERS SET CITY = ? WHERE CUSTOMER_ID = ?";
    int affected = db.ExecuteNonQuery(sql, new List<object> { newCity, customerId });
    Console.WriteLine($"  ✓ Updated {affected} customer row(s)");
}

#pragma warning disable CS8321
static void UpdateCustomer(AS400Helper db, string lib, int customerId,
    string firstName, string lastName, string email, string phone, string city)
{
    string sql = $@"UPDATE {lib}.CUSTOMERS
                    SET FIRST_NAME = ?, LAST_NAME = ?, EMAIL = ?, PHONE = ?, CITY = ?
                    WHERE CUSTOMER_ID = ?";
    int affected = db.ExecuteNonQuery(sql, new List<object> { firstName, lastName, email, phone, city, customerId });
    Console.WriteLine($"  ✓ Updated customer ID {customerId} ({affected} row)");
}
#pragma warning restore CS8321

static void DeleteCustomerById(AS400Helper db, string lib, int customerId)
{
    string sql = $"DELETE FROM {lib}.CUSTOMERS WHERE CUSTOMER_ID = ?";
    int affected = db.ExecuteNonQuery(sql, new List<object> { customerId });
    Console.WriteLine($"  ✓ Deleted {affected} customer row(s) (ID={customerId})");
}


// ─────────────────────────────────────────
//  ORDERS CRUD METHODS
// ─────────────────────────────────────────

static void InsertOrder(AS400Helper db, string lib, int customerId,
    decimal totalAmount, string status, string description)
{
    string sql = $@"INSERT INTO {lib}.ORDERS (CUSTOMER_ID, TOTAL_AMOUNT, STATUS, DESCRIPTION)
                    VALUES (?, ?, ?, ?)";
    int affected = db.ExecuteNonQuery(sql, new List<object> { customerId, totalAmount, status, description });
    Console.WriteLine($"  ✓ Inserted order for CustomerID={customerId} ({affected} row)");
}

static DataTable GetAllOrders(AS400Helper db, string lib)
{
    string sql = $"SELECT * FROM {lib}.ORDERS ORDER BY ORDER_ID";
    return db.ExecuteQuery(sql);
}

#pragma warning disable CS8321
static DataTable GetOrderById(AS400Helper db, string lib, int orderId)
{
    string sql = $"SELECT * FROM {lib}.ORDERS WHERE ORDER_ID = ?";
    return db.ExecuteQuery(sql, new List<object> { orderId });
}
#pragma warning restore CS8321

static DataTable GetOrdersByCustomerId(AS400Helper db, string lib, int customerId)
{
    string sql = $"SELECT * FROM {lib}.ORDERS WHERE CUSTOMER_ID = ? ORDER BY ORDER_ID";
    return db.ExecuteQuery(sql, new List<object> { customerId });
}

static void UpdateOrderStatus(AS400Helper db, string lib, int orderId, string newStatus)
{
    string sql = $"UPDATE {lib}.ORDERS SET STATUS = ? WHERE ORDER_ID = ?";
    int affected = db.ExecuteNonQuery(sql, new List<object> { newStatus, orderId });
    Console.WriteLine($"  ✓ Updated order ID {orderId} status to '{newStatus}' ({affected} row)");
}

#pragma warning disable CS8321
static void UpdateOrder(AS400Helper db, string lib, int orderId,
    decimal totalAmount, string status, string description)
{
    string sql = $@"UPDATE {lib}.ORDERS
                    SET TOTAL_AMOUNT = ?, STATUS = ?, DESCRIPTION = ?
                    WHERE ORDER_ID = ?";
    int affected = db.ExecuteNonQuery(sql, new List<object> { totalAmount, status, description, orderId });
    Console.WriteLine($"  ✓ Updated order ID {orderId} ({affected} row)");
}
#pragma warning restore CS8321

static void DeleteOrderById(AS400Helper db, string lib, int orderId)
{
    string sql = $"DELETE FROM {lib}.ORDERS WHERE ORDER_ID = ?";
    int affected = db.ExecuteNonQuery(sql, new List<object> { orderId });
    Console.WriteLine($"  ✓ Deleted {affected} order row(s) (ID={orderId})");
}

#pragma warning disable CS8321
static void DeleteOrdersByCustomerId(AS400Helper db, string lib, int customerId)
{
    string sql = $"DELETE FROM {lib}.ORDERS WHERE CUSTOMER_ID = ?";
    int affected = db.ExecuteNonQuery(sql, new List<object> { customerId });
    Console.WriteLine($"  ✓ Deleted {affected} order(s) for CustomerID={customerId}");
}
#pragma warning restore CS8321


// ─────────────────────────────────────────
//  JOIN: CUSTOMERS + ORDERS
// ─────────────────────────────────────────

static DataTable GetCustomersWithOrders(AS400Helper db, string lib)
{
    string sql = $@"
        SELECT
            C.CUSTOMER_ID,
            C.FIRST_NAME,
            C.LAST_NAME,
            C.CITY,
            O.ORDER_ID,
            O.ORDER_DATE,
            O.TOTAL_AMOUNT,
            O.STATUS,
            O.DESCRIPTION
        FROM {lib}.CUSTOMERS C
        INNER JOIN {lib}.ORDERS O ON C.CUSTOMER_ID = O.CUSTOMER_ID
        ORDER BY C.CUSTOMER_ID, O.ORDER_ID";

    return db.ExecuteQuery(sql);
}

// ─────────────────────────────────────────
//  HELPER: Print DataTable to Console
// ─────────────────────────────────────────
static void PrintTable(DataTable table)
{
    if (table.Rows.Count == 0)
    {
        Console.WriteLine("  (no rows returned)");
        return;
    }

    foreach (DataColumn col in table.Columns)
        Console.Write($"{col.ColumnName,-20}");
    Console.WriteLine();
    Console.WriteLine(new string('-', table.Columns.Count * 20));

    foreach (DataRow row in table.Rows)
    {
        foreach (DataColumn col in table.Columns)
            Console.Write($"{Convert.ToString(row[col]),-20}");
        Console.WriteLine();
    }

    Console.WriteLine($"\n  Total rows returned: {table.Rows.Count}");
}

// ─────────────────────────────────────────
//  AUTO-CREATE TABLE IN POSTGRESQL
// ─────────────────────────────────────────
#pragma warning disable CS8321
static void CreatePostgresTableIfNotExists(NpgsqlConnection conn, string pgTable, DataTable schema)
{
    var columns = new List<string>();
    foreach (DataColumn col in schema.Columns)
    {
        string pgType = MapToPgType(col.DataType);
        columns.Add($"\"{col.ColumnName.ToLower()}\" {pgType}");
    }

    string createSql = $@"
        CREATE TABLE IF NOT EXISTS {pgTable} (
            id SERIAL PRIMARY KEY,
            {string.Join(",\n            ", columns)}
        );";

    using var cmd = new NpgsqlCommand(createSql, conn);
    cmd.ExecuteNonQuery();
    Console.WriteLine($"✓ Table ready: {pgTable}");
}
#pragma warning restore CS8321

// ─────────────────────────────────────────
//  INSERT DataTable ROWS → POSTGRESQL
// ─────────────────────────────────────────
#pragma warning disable CS8321
static int InsertDataToPostgres(NpgsqlConnection conn, string pgTable, DataTable table)
{
    if (table.Rows.Count == 0)
    {
        Console.WriteLine("  (no rows to insert)");
        return 0;
    }

    var colNames = new List<string>();
    var placeholders = new List<string>();

    foreach (DataColumn col in table.Columns)
        colNames.Add($"\"{col.ColumnName.ToLower()}\"");

    for (int i = 1; i <= table.Columns.Count; i++)
        placeholders.Add($"@p{i}");

    string insertSql = $@"
        INSERT INTO {pgTable} ({string.Join(", ", colNames)})
        VALUES ({string.Join(", ", placeholders)})";

    int totalInserted = 0;

    using var transaction = conn.BeginTransaction();
    try
    {
        using var cmd = new NpgsqlCommand(insertSql, conn, transaction);

        for (int i = 0; i < table.Columns.Count; i++)
            cmd.Parameters.Add(new NpgsqlParameter($"p{i + 1}", DBNull.Value));

        foreach (DataRow row in table.Rows)
        {
            for (int i = 0; i < table.Columns.Count; i++)
                cmd.Parameters[i].Value = row[i] == DBNull.Value ? DBNull.Value : row[i];

            cmd.ExecuteNonQuery();
            totalInserted++;
            Console.Write($"\r  Inserting... {totalInserted}/{table.Rows.Count} rows");
        }

        transaction.Commit();
        Console.WriteLine();
    }
    catch
    {
        transaction.Rollback();
        Console.WriteLine("\n  Transaction rolled back.");
        throw;
    }

    return totalInserted;
}
#pragma warning restore CS8321

// ─────────────────────────────────────────
//  MAP .NET TYPES → POSTGRESQL TYPES
// ─────────────────────────────────────────
static string MapToPgType(Type type)
{
    return type switch
    {
        Type t when t == typeof(int) => "INTEGER",
        Type t when t == typeof(long) => "BIGINT",
        Type t when t == typeof(short) => "SMALLINT",
        Type t when t == typeof(decimal) => "NUMERIC",
        Type t when t == typeof(float) => "REAL",
        Type t when t == typeof(double) => "DOUBLE PRECISION",
        Type t when t == typeof(bool) => "BOOLEAN",
        Type t when t == typeof(DateTime) => "TIMESTAMP",
        Type t when t == typeof(Guid) => "UUID",
        Type t when t == typeof(byte[]) => "BYTEA",
        _ => "TEXT"
    };
}

// ─────────────────────────────────────────
//  AS400 HELPER CLASS
// ─────────────────────────────────────────
public class AS400Helper : IDisposable
{
    private readonly string _connectionString;
    private OdbcConnection? _connection;

    public AS400Helper(string host, string userId, string password, string library)
    {
        _connectionString =
            $"Driver={{iSeries Access ODBC Driver}};" +
            $"System={host};" +
            $"Uid={userId};" +
            $"Pwd={password};" +
            $"DefaultLibraries={library};";
    }

    public void Connect()
    {
        _connection = new OdbcConnection(_connectionString);
        _connection.Open();
        Console.WriteLine($"✓ AS400 connected | Server: {_connection.DataSource} | Driver: {_connection.Driver}\n");
    }

    public DataTable ExecuteQuery(string sql, List<object>? parameters = null)
    {
        EnsureConnected();
        DataTable result = new DataTable();
        using var command = new OdbcCommand(sql, _connection);
        AddParameters(command, parameters);
        using var adapter = new OdbcDataAdapter(command);
        adapter.Fill(result);
        return result;
    }

    public int ExecuteNonQuery(string sql, List<object>? parameters = null, OdbcTransaction? transaction = null)
    {
        EnsureConnected();
        using var command = new OdbcCommand(sql, _connection);
        if (transaction != null) command.Transaction = transaction;
        AddParameters(command, parameters);
        return command.ExecuteNonQuery();
    }

    public object? ExecuteScalar(string sql, List<object>? parameters = null)
    {
        EnsureConnected();
        using var command = new OdbcCommand(sql, _connection);
        AddParameters(command, parameters);
        return command.ExecuteScalar();
    }

    public void ExecuteInTransaction(Action<OdbcConnection, OdbcTransaction> actions)
    {
        EnsureConnected();
        using OdbcTransaction transaction = _connection!.BeginTransaction();
        try
        {
            actions(_connection, transaction);
            transaction.Commit();
            Console.WriteLine("Transaction committed.");
        }
        catch
        {
            transaction.Rollback();
            Console.WriteLine("Transaction rolled back.");
            throw;
        }
    }

    private void EnsureConnected()
    {
        if (_connection == null || _connection.State != ConnectionState.Open)
            throw new InvalidOperationException("Database is not connected. Call Connect() first.");
    }

    private static void AddParameters(OdbcCommand command, List<object>? parameters)
    {
        if (parameters == null) return;
        foreach (var param in parameters)
            command.Parameters.AddWithValue("?", param ?? DBNull.Value);
    }

    public void Dispose()
    {
        if (_connection != null)
        {
            _connection.Close();
            _connection.Dispose();
            Console.WriteLine("\nAS400 Connection closed.");
        }
    }
}

// ─────────────────────────────────────────
//  CONFIGURATION POCOs
// ─────────────────────────────────────────
public class AS400Settings
{
    public string Host { get; set; } = default!;
    public string User { get; set; } = default!;
    public string Password { get; set; } = default!;
    public string Library { get; set; } = default!;
    public string Table { get; set; } = default!;
}

public class PostgresSettings
{
    public string ConnectionString { get; set; } = default!;
    public string Table { get; set; } = default!;
}