using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MergeDesk.Core;

namespace MergeDesk.Infrastructure;

public sealed class LiveSendRunner(ISendLedger ledger)
{
    public static async Task<string> FingerprintAsync(MergedMessage message, string destination, CancellationToken token = default)
    {
        var files = new List<object>();
        foreach (var path in message.Attachments)
        {
            await using var file = File.OpenRead(path);
            files.Add(new { Name = Path.GetFileName(path), Hash = Convert.ToHexString(await SHA256.HashDataAsync(file, token)) });
        }
        var account = destination.Contains('/') ? destination.Split('/')[1].Trim() : destination;
        var data = JsonSerializer.Serialize(new { Account = account.ToLowerInvariant(), To = Addresses(message.To), Cc = Addresses(message.Cc), Bcc = Addresses(message.Bcc), message.Subject,
            BodyKind = string.IsNullOrWhiteSpace(message.Html) ? "Text" : "HTML", Body = string.IsNullOrWhiteSpace(message.Html) ? message.Text : message.Html, Files = files });
        if (message.InlineImages is { Count: > 0 })
            data += JsonSerializer.Serialize(message.InlineImages.Select(i => new { i.ContentId, i.MediaType, Hash = Convert.ToHexString(SHA256.HashData(i.Bytes)) }));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(data)));
    }
    private static string[] Addresses(string value) => MergeEngine.SplitAddresses(value).Select(s => s.ToLowerInvariant()).Order().ToArray();
    public async Task RunAsync(IReadOnlyList<MergedMessage> messages, IMailDelivery provider, string batchId, int delaySeconds,
        Func<RunResult, Task> report, CancellationToken token = default)
    {
        var stop = false;
        foreach (var message in messages)
        {
            var operation = $"{batchId}-row-{message.Row}";
            RunResult Result(string status, string detail, string? id = null) => new(message.Row, message.To, message.Subject, status, detail, DateTimeOffset.Now, id, operation);
            if (stop || token.IsCancellationRequested)
            { await report(Result(token.IsCancellationRequested ? "Cancelled" : "Not attempted", "Batch stopped; no send attempted for this row.")); continue; }
            DeliveryAttempt? attempt = null;
            var attachmentLocks = new List<FileStream>();
            try
            {
                foreach (var path in message.Attachments) attachmentLocks.Add(File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read));
                // Attachment bytes are part of the identity. Prepare all fields before claiming.
                var key = await FingerprintAsync(message, provider.Destination, token);
                var reservation = await ledger.ReserveAsync(key, operation);
                attempt = reservation.Attempt;
                if (!reservation.Owned)
                {
                    var submitted = attempt.State == "Submitted";
                    await report(Result(submitted ? "Already submitted" : "Needs review", submitted ? "Identical message was previously submitted; skipped." : $"Previous operation: {attempt.State}. {attempt.Detail} Review Outlook and the send record before retrying.", attempt.DraftId));
                    if (!submitted) stop = true;
                    continue;
                }
                if (token.IsCancellationRequested)
                { await ledger.SaveAsync(attempt with { State = attempt.DraftId == null ? "Not sent" : "Prepared" }); token.ThrowIfCancellationRequested(); }
                if (attempt.DraftId == null)
                {
                    var receipt = await provider.CreateDraftAsync(message, operation, token);
                    attempt = attempt with { State = "Prepared", DraftId = receipt.Id };
                    await ledger.SaveAsync(attempt);
                }
                if (token.IsCancellationRequested) token.ThrowIfCancellationRequested();
                // Persist uncertainty BEFORE sending. A crash at any later point blocks repeats.
                attempt = attempt with { State = "Submitting" }; await ledger.SaveAsync(attempt);
                await provider.SubmitDraftAsync(attempt.DraftId!, token);
                attempt = attempt with { State = "Submitted" }; await ledger.SaveAsync(attempt);
                await report(Result(provider.SubmissionStatus, "Submission confirmed; recipient delivery is not verified.", attempt.DraftId));
                if (delaySeconds > 0)
                    try { await Task.Delay(TimeSpan.FromSeconds(delaySeconds), token); }
                    catch (OperationCanceledException) { stop = true; }
            }
            catch (SubmissionRejectedException ex)
            {
                if (attempt != null) await ledger.SaveAsync(attempt with { State = attempt.DraftId == null ? "Not sent" : "Prepared", Detail = ex.Message, RetryAfter = ex.RetryAfter });
                await report(Result("Not sent", ex.Message + " Batch stopped. A later run can reuse the saved draft after the restriction clears.", attempt?.DraftId)); stop = true;
            }
            catch (HttpRequestException ex) when (attempt?.DraftId == null && (int?)ex.StatusCode is >= 400 and < 500 && (int?)ex.StatusCode != 408)
            {
                if (attempt != null) await ledger.SaveAsync(attempt with { State = "Not sent", Detail = ex.Message,
                    RetryAfter = (ex as GraphRateLimitException)?.RetryAfter });
                await report(Result("Not sent", ex.Message + " Draft request rejected. Batch stopped; resolve the restriction before trying again.")); stop = true;
            }
            catch (OperationCanceledException) when (attempt?.State is not "Submitting")
            { await report(Result("Cancelled", "Send not attempted. A prepared draft can be reused on a later confirmed run.", attempt?.DraftId)); stop = true; }
            catch (Exception ex)
            {
                // Do not clear a durable Drafting/Submitting marker when outcome is unknown.
                await report(Result(attempt?.State == "Submitted" ? provider.SubmissionStatus : "Needs review", ex.Message + " Batch stopped; check Outlook before retrying.", attempt?.DraftId)); stop = true;
            }
            finally { foreach (var file in attachmentLocks) file.Dispose(); }
        }
    }
}
