using System.Text.Json;
using Microsoft.Data.Sqlite;
using TechRiders.Application;
using TechRiders.Domain;

namespace TechRiders.Infrastructure;

public sealed class SqliteOrderStore : IOrderStore
{
    private readonly string connectionString;

    public SqliteOrderStore(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        connectionString = new SqliteConnectionStringBuilder { DataSource = path, DefaultTimeout = 30 }.ToString();
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS orders (id TEXT PRIMARY KEY, document TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS cancellations (key TEXT PRIMARY KEY, document TEXT NOT NULL);
            """;
        command.ExecuteNonQuery();
        InTransaction(s =>
        {
            foreach (var order in OrderRules.Seed())
                if (s.Find(order.Id) is null) s.Save(order);
            return true;
        });
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(connectionString);
        try
        {
            connection.Open();
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    public T InTransaction<T>(Func<IOrderSession, T> action)
    {
        using var span = Telemetry.Source.StartActivity("database.transaction");
        span?.SetTag("db.system.name", "sqlite");
        using var connection = Open();
        using var transaction = connection.BeginTransaction(deferred: false);
        var result = action(new Session(connection, transaction));
        transaction.Commit();
        return result;
    }

    public void ResetDemoData()
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction(deferred: false);
        var session = new Session(connection, transaction);
        foreach (var order in OrderRules.Seed())
        {
            var old = session.Find(order.Id);
            session.Save(order with { Version = (old?.Version ?? 0) + 1 });
        }
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM cancellations WHERE key LIKE 'demo:%'";
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    private sealed class Session(SqliteConnection connection, SqliteTransaction transaction) : IOrderSession
    {
        private static T ReadDocument<T>(string json) where T : class =>
            JsonSerializer.Deserialize<T>(json) ??
            throw new InvalidDataException($"Stored {typeof(T).Name} document must not be JSON null.");

        private SqliteCommand Command(string sql, params (string Key, object Value)[] parameters)
        {
            var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            foreach (var (key, value) in parameters) command.Parameters.AddWithValue(key, value);
            return command;
        }

        public IReadOnlyList<Order> All()
        {
            using var command = Command("SELECT document FROM orders ORDER BY id");
            using var reader = command.ExecuteReader();
            var orders = new List<Order>();
            while (reader.Read()) orders.Add(ReadDocument<Order>(reader.GetString(0)));
            return orders;
        }

        public Order? Find(string id)
        {
            using var command = Command("SELECT document FROM orders WHERE id=$id", ("$id", id));
            return command.ExecuteScalar() is string json ? ReadDocument<Order>(json) : null;
        }

        public void Save(Order order)
        {
            using var command = Command("INSERT INTO orders(id,document) VALUES($id,$doc) ON CONFLICT(id) DO UPDATE SET document=excluded.document",
                ("$id", order.Id), ("$doc", JsonSerializer.Serialize(order)));
            command.ExecuteNonQuery();
        }

        public void Delete(string id)
        {
            using var command = Command("DELETE FROM orders WHERE id=$id", ("$id", id));
            command.ExecuteNonQuery();
        }

        public StoredCancellation? FindCancellation(string key)
        {
            using var command = Command("SELECT document FROM cancellations WHERE key=$key", ("$key", key));
            return command.ExecuteScalar() is string json ? ReadDocument<StoredCancellation>(json) : null;
        }

        public void SaveCancellation(string key, StoredCancellation value)
        {
            using var command = Command("INSERT INTO cancellations(key,document) VALUES($key,$doc)",
                ("$key", key), ("$doc", JsonSerializer.Serialize(value)));
            command.ExecuteNonQuery();
        }
    }
}
