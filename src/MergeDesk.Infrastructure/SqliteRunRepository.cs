using System.Text.Json;
using Microsoft.Data.Sqlite;
using MergeDesk.Core;

namespace MergeDesk.Infrastructure;

public sealed class SqliteRunRepository(string databasePath) : IRunRepository
{
    private async Task<SqliteConnection> OpenAsync(CancellationToken token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(databasePath))!);
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString());
        try
        {
            await connection.OpenAsync(token);
            using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE IF NOT EXISTS runs (id TEXT PRIMARY KEY, started TEXT NOT NULL, payload TEXT NOT NULL)";
            await command.ExecuteNonQueryAsync(token);
            return connection;
        }
        catch { await connection.DisposeAsync(); throw; }
    }
    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        // Only display history is removed. Durable send ledgers live in separate databases.
        command.CommandText = "DELETE FROM runs";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
    public async Task SaveAsync(RunRecord run, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT OR REPLACE INTO runs(id, started, payload) VALUES ($id, $started, $payload)";
        command.Parameters.AddWithValue("$id", run.Id);
        command.Parameters.AddWithValue("$started", run.Started.ToString("O"));
        command.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(run));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
    public async Task<IReadOnlyList<RunRecord>> LoadAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload FROM runs ORDER BY started DESC LIMIT 100";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var runs = new List<RunRecord>();
        while (await reader.ReadAsync(cancellationToken))
            if (JsonSerializer.Deserialize<RunRecord>(reader.GetString(0)) is { } run) runs.Add(run);
        return runs;
    }
}
