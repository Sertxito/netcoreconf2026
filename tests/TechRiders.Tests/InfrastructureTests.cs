using Microsoft.Data.Sqlite;
using TechRiders.Domain;
using TechRiders.Infrastructure;

namespace TechRiders.Tests;

public sealed class InfrastructureTests : IAsyncLifetime
{
    private readonly string directory;
    private readonly string databasePath;
    private readonly SqliteOrderStore store;

    public InfrastructureTests()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "TechRiders.slnx")))
            root = root.Parent;
        directory = Path.Combine(root?.FullName ?? throw new InvalidOperationException("Solution root not found."),
            ".local", "tests", $"store-{Guid.NewGuid():N}");
        databasePath = Path.Combine(directory, "store.db");
        store = new(databasePath);
    }

    [Fact]
    public void FailedTransactionRollsBackOrderAndIdempotencyTogether()
    {
        var before = store.InTransaction(session => session.Find("ORD-1004"))!;
        Assert.Throws<InvalidOperationException>(() => store.InTransaction<bool>(session =>
        {
            session.Save(before with { Status = OrderStatus.Cancelled, Version = before.Version + 1 });
            session.SaveCancellation("demo:rollback", new("hash", "actor",
                new(before.Id, "Pending", "Cancelled", false)));
            throw new InvalidOperationException("Simulated failure before commit.");
        }));
        Assert.Equal(before, store.InTransaction(session => session.Find(before.Id)));
        Assert.Null(store.InTransaction(session => session.FindCancellation("demo:rollback")));
    }

    [Theory]
    [InlineData("all")]
    [InlineData("find")]
    [InlineData("cancellation")]
    public void StoredJsonNullIsCorruptionNotAMissingRecord(string read)
    {
        using (var connection = new SqliteConnection(ConnectionString))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = read == "cancellation"
                ? "INSERT INTO cancellations(key,document) VALUES('demo:corrupt','null')"
                : "UPDATE orders SET document='null' WHERE id='ORD-1004'";
            command.ExecuteNonQuery();
        }
        var error = Assert.Throws<InvalidDataException>(() => store.InTransaction<object?>(session => read switch
        {
            "all" => session.All(),
            "find" => session.Find("ORD-1004"),
            _ => session.FindCancellation("demo:corrupt")
        }));
        Assert.Contains("must not be JSON null", error.Message);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var connection = new SqliteConnection(ConnectionString);
        SqliteConnection.ClearPool(connection);
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
                return;
            }
            catch (IOException) when (attempt < 9)
            {
                await Task.Delay(100);
            }
        }
    }

    private string ConnectionString =>
        new SqliteConnectionStringBuilder { DataSource = databasePath, DefaultTimeout = 30 }.ToString();
}
