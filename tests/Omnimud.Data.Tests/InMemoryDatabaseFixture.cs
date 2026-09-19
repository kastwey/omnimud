using Microsoft.Data.Sqlite;
using Omnimud.Data.Migrations;

namespace Omnimud.Data.Tests;

public sealed class InMemoryDatabaseFixture : IAsyncLifetime, IDbConnectionFactory
{
    private SqliteConnection _keepAliveConnection = null!;
    private string _connectionString = null!;

    public async Task InitializeAsync()
    {
        var dbName = $"TestDb_{Guid.NewGuid():N}";
        _connectionString = $"Data Source={dbName};Mode=Memory;Cache=Shared";

        // Keep-alive connection to preserve the in-memory DB
        _keepAliveConnection = new SqliteConnection(_connectionString);
        await _keepAliveConnection.OpenAsync();

        // Run migrations
        await MigrationRunner.RunAsync(_keepAliveConnection);
    }

    public SqliteConnection Create()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA foreign_keys=ON;";
        cmd.ExecuteNonQuery();

        return connection;
    }

    public Task DisposeAsync()
    {
        _keepAliveConnection?.Dispose();
        return Task.CompletedTask;
    }
}
