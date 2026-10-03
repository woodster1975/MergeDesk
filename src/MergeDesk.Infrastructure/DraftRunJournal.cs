using System.Text.Json;
using MergeDesk.Core;

namespace MergeDesk.Infrastructure;

/// <summary>Keep disk/database work off the WPF dispatcher and bound how long a batch waits for it.</summary>
public sealed class DraftRunJournal(IRunRepository repository, TimeSpan? timeout = null)
{
    private readonly TimeSpan timeout = timeout ?? TimeSpan.FromSeconds(20);
    public async Task SaveAsync(RunRecord snapshot)
    {
        var work = Task.Run(async () =>
        {
            Directory.CreateDirectory(snapshot.OutputPath);
            var temp = Path.Combine(snapshot.OutputPath, "results-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                // Synchronous local IO on a worker avoids a dependency on UI-thread IO continuations.
                File.WriteAllText(temp, JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true }));
                File.Move(temp, Path.Combine(snapshot.OutputPath, "results.json"), true);
                await repository.SaveAsync(snapshot).ConfigureAwait(false);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        });
        // Observe late faults if an OS/database operation outlives the timeout. Never retry a
        // journal write or start another mailbox operation in this batch after that timeout.
        _ = work.ContinueWith(t => _ = t.Exception, CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        try { await work.WaitAsync(timeout).ConfigureAwait(false); }
        catch (TimeoutException ex)
        { throw new IOException("Saving the local results log took too long. No further drafts were attempted. Check the results folder and Outlook before retrying.", ex); }
    }
}
