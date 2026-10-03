namespace MergeDesk.Core;

public sealed class DraftBatchRunner
{
    public async Task<IReadOnlyList<RunResult>> CreateAsync(IReadOnlyList<MergedMessage> messages, IMailDraftCreator creator,
        string batchId, Action<RunResult>? report = null, CancellationToken cancellationToken = default,
        Func<MergedMessage, string, Task>? beforeAttempt = null, Func<RunResult, Task>? persist = null)
    {
        var results = new List<RunResult>();
        var stoppedForReview = false;
        foreach (var message in messages)
        {
            var operationId = $"{batchId}-row-{message.Row}";
            RunResult result;
            if (stoppedForReview)
                result = new(message.Row, message.To, message.Subject, "Not attempted", "Batch stopped. Review the previous uncertain or incomplete draft before creating another batch.", DateTimeOffset.Now, OperationId: operationId);
            else if (cancellationToken.IsCancellationRequested)
                result = new(message.Row, message.To, message.Subject, "Cancelled", "No draft attempted", DateTimeOffset.Now, OperationId: operationId);
            else
            {
                // Journal before the external write. A logging failure stops the batch.
                if (beforeAttempt != null) await beforeAttempt(message, operationId);
                try
                {
                    var receipt = await creator.CreateDraftAsync(message, operationId, cancellationToken);
                    result = new(message.Row, message.To, message.Subject, "Draft created", receipt.Location, DateTimeOffset.Now, receipt.Id, operationId);
                }
                catch (DraftCreationException ex)
                {
                    result = new(message.Row, message.To, message.Subject, ex.MayExist ? "Needs review" : "Failed", ex.Message, DateTimeOffset.Now, ex.DraftId, operationId);
                    stoppedForReview = ex.MayExist;
                }
                catch (OperationCanceledException)
                {
                    result = new(message.Row, message.To, message.Subject, "Cancelled", "No draft confirmed", DateTimeOffset.Now, OperationId: operationId);
                }
                catch (Exception ex)
                {
                    result = new(message.Row, message.To, message.Subject, "Failed", ex.Message, DateTimeOffset.Now, OperationId: operationId);
                }
            }
            results.Add(result); report?.Invoke(result);
            if (persist != null) await persist(result);
        }
        return results;
    }
}
