using System.Net;
using System.Net.Http.Headers;
using MergeDesk.Core;

namespace MergeDesk.Infrastructure;

public sealed class GraphMailSender(GraphDraftCreator drafts, HttpClient http, Func<CancellationToken, Task<string>> getToken) : IMailDelivery
{
    public string Destination => drafts.Destination;
    public string SubmissionStatus => "Accepted by Microsoft";
    public Task<DraftReceipt> CreateDraftAsync(MergedMessage message, string operationId, CancellationToken cancellationToken = default) => drafts.CreateDraftAsync(message, operationId, cancellationToken);
    public async Task<string> SendAsync(MergedMessage message, CancellationToken cancellationToken = default)
    { var draft = await CreateDraftAsync(message, Guid.NewGuid().ToString("N"), cancellationToken); await SubmitDraftAsync(draft.Id, cancellationToken); return draft.Id; }
    public async Task SubmitDraftAsync(string draftId, CancellationToken token = default)
    {
        // Acquire authentication before submitting. A rejected sign-in cannot have sent mail.
        string accessToken;
        try { accessToken = await getToken(token); }
        catch (Exception ex) { throw new SubmissionRejectedException("Sign in again before sending. " + ex.Message); }
        if (token.IsCancellationRequested) throw new SubmissionRejectedException("Cancelled before submission.");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"https://graph.microsoft.com/v1.0/me/messages/{Uri.EscapeDataString(draftId)}/send");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.TryAddWithoutValidation("Prefer", "IdType=\"ImmutableId\"");
        using var response = await http.SendAsync(request, token);
        if (response.StatusCode == HttpStatusCode.Accepted) return;
        if ((int)response.StatusCode is >= 400 and < 500 && response.StatusCode != HttpStatusCode.RequestTimeout)
        {
            var wait = response.Headers.RetryAfter?.Delta ?? (response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow);
            var retry = response.StatusCode == HttpStatusCode.TooManyRequests ? DateTimeOffset.UtcNow + (wait > TimeSpan.Zero ? wait.Value : TimeSpan.FromMinutes(1)) : (DateTimeOffset?)null;
            throw new SubmissionRejectedException($"Microsoft rejected submission ({(int)response.StatusCode})." + (retry != null ? $" Wait until {retry:O} before retrying." : " Check account permission and message limits."), retry);
        }
        throw new IOException($"Microsoft did not confirm submission ({(int)response.StatusCode}). The message may have been sent.");
    }
}
