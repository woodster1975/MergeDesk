using System.Text.Json;
using MergeDesk.Core;

namespace MergeDesk.Infrastructure;

/// <summary>Atomic per-session recovery files. Leases keep open instances and pending restores separate.</summary>
public sealed class JsonProjectRecoveryStore(string directory) : IProjectRecoveryStore, IDisposable
{
    private readonly string sessionId = Guid.NewGuid().ToString("N");
    private readonly SemaphoreSlim gate = new(1);
    private FileStream? sessionLease, pendingLease;
    private string? pendingPath;
    private string SessionPath => Path.Combine(directory, sessionId + ".json");
    public async Task<RecoverySnapshot?> LoadAsync()
    {
        await gate.WaitAsync();
        try
        {
            if (!Directory.Exists(directory)) return null;
            foreach (var path in Directory.EnumerateFiles(directory, "*.json").OrderByDescending(File.GetLastWriteTimeUtc))
            {
                if (path == SessionPath) continue;
                FileStream? lease = null;
                try
                {
                    lease = new FileStream(Path.ChangeExtension(path, ".lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                    if (new FileInfo(path).Length > 64 * 1024 * 1024) throw new InvalidDataException("Recovery file is too large.");
                    var snapshot = JsonSerializer.Deserialize<RecoverySnapshot>(await File.ReadAllTextAsync(path));
                    if (snapshot?.Version != 1 || snapshot.Project?.Template == null || snapshot.Project.Mappings == null)
                        throw new InvalidDataException("Unsupported or incomplete recovery file.");
                    pendingLease?.Dispose(); pendingLease = lease; pendingPath = path; lease = null;
                    return snapshot;
                }
                catch (JsonException) { /* Keep damaged files; try an earlier complete snapshot. */ }
                catch (InvalidDataException) { }
                catch (IOException) { /* An open instance owns this file, or it cannot be read. */ }
                finally { lease?.Dispose(); }
            }
            return null;
        }
        finally { gate.Release(); }
    }
    public async Task SaveAsync(MergeProject project)
    {
        await gate.WaitAsync();
        try
        {
            await Task.Run(async () =>
            {
                Directory.CreateDirectory(directory);
                sessionLease ??= new FileStream(Path.ChangeExtension(SessionPath, ".lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                var temporary = SessionPath + ".tmp";
                try
                {
                    var bytes = JsonSerializer.SerializeToUtf8Bytes(new RecoverySnapshot(DateTimeOffset.UtcNow, project));
                    await using (var file = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                    { await file.WriteAsync(bytes); await file.FlushAsync(); file.Flush(flushToDisk: true); }
                    File.Move(temporary, SessionPath, overwrite: true);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            });
        }
        finally { gate.Release(); }
    }
    public async Task AcknowledgeAsync()
    {
        await gate.WaitAsync();
        try
        {
            if (pendingPath != null) File.Delete(pendingPath);
            pendingPath = null; pendingLease?.Dispose(); pendingLease = null;
        }
        finally { gate.Release(); }
    }
    public void Dispose() { pendingLease?.Dispose(); sessionLease?.Dispose(); }
}
