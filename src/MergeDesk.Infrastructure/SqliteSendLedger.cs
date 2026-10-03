using System.Text.Json;
using MergeDesk.Core;
using Microsoft.Data.Sqlite;

namespace MergeDesk.Infrastructure;

public sealed class SqliteSendLedger(string path) : ISendLedger
{
    private SqliteConnection Open()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, DefaultTimeout = 5 }.ToString());
        try
        {
            connection.Open(); using var cmd = connection.CreateCommand();
            cmd.CommandText = "CREATE TABLE IF NOT EXISTS send_ledger (key TEXT PRIMARY KEY, payload TEXT NOT NULL)";
            cmd.ExecuteNonQuery(); return connection;
        }
        catch { connection.Dispose(); throw; }
    }
    public async Task<DeliveryReservation> ReserveAsync(string key, string operationId) => await Task.Run(() =>
    {
        using var connection = Open(); using var tx = connection.BeginTransaction(deferred: false);
        using var read = connection.CreateCommand(); read.Transaction = tx;
        read.CommandText = "SELECT payload FROM send_ledger WHERE key=$key"; read.Parameters.AddWithValue("$key", key);
        var old = read.ExecuteScalar() is string json ? JsonSerializer.Deserialize<DeliveryAttempt>(json) : null;
        if (old != null && (old.State is not ("Prepared" or "Not sent") || old.RetryAfter > DateTimeOffset.UtcNow))
        { tx.Commit(); return new DeliveryReservation(old, false); }
        var attempt = new DeliveryAttempt(key, operationId, "Drafting", old?.DraftId);
        Write(connection, tx, attempt); tx.Commit(); return new DeliveryReservation(attempt, true);
    }).WaitAsync(TimeSpan.FromSeconds(20));
    public async Task SaveAsync(DeliveryAttempt attempt) => await Task.Run(() =>
    {
        using var connection = Open(); using var tx = connection.BeginTransaction(deferred: false);
        using var read = connection.CreateCommand(); read.Transaction = tx;
        read.CommandText = "SELECT payload FROM send_ledger WHERE key=$key"; read.Parameters.AddWithValue("$key", attempt.Key);
        var current = JsonSerializer.Deserialize<DeliveryAttempt>((string?)read.ExecuteScalar() ?? "null");
        if (current?.OperationId != attempt.OperationId) throw new IOException("The send record is owned by another operation. Sending stopped.");
        Write(connection, tx, attempt); tx.Commit();
    }).WaitAsync(TimeSpan.FromSeconds(20));
    private static void Write(SqliteConnection connection, SqliteTransaction tx, DeliveryAttempt attempt)
    {
        using var cmd = connection.CreateCommand(); cmd.Transaction = tx;
        cmd.CommandText = "INSERT OR REPLACE INTO send_ledger(key,payload) VALUES ($key,$payload)";
        cmd.Parameters.AddWithValue("$key", attempt.Key); cmd.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(attempt)); cmd.ExecuteNonQuery();
    }
}
