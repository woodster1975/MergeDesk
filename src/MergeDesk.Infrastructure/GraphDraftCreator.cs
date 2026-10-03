using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using MergeDesk.Core;

namespace MergeDesk.Infrastructure;

/// <summary>Delegated Microsoft Graph draft creation. This class never calls a send endpoint.</summary>
public sealed class GraphDraftCreator(HttpClient http, Func<CancellationToken, Task<string>> getToken, string mailbox) : IMailDraftCreator
{
    public const long MaximumAttachmentBytes = 150L * 1024 * 1024;
    private const long SmallAttachmentBytes = 3L * 1024 * 1024;
    private const int ChunkBytes = 10 * 320 * 1024; // Below 4 MB and a multiple of 320 KiB.
    private const string GraphRoot = "https://graph.microsoft.com/v1.0/";
    public string Destination => $"Microsoft mailbox / {mailbox} / Drafts";

    public async Task ValidateConnectionAsync(CancellationToken cancellationToken = default)
    {
        using var response = await GraphRequestAsync(HttpMethod.Get, "me/mailFolders/drafts?$select=id,displayName", null, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }
    public async Task<DraftReceipt> CreateDraftAsync(MergedMessage message, string operationId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Verify and hold attachment handles before any cloud write. A missing or oversized
        // file therefore cannot leave a draft without its expected attachments.
        var files = new List<(string Path, FileStream Stream)>();
        string? draftId = null;
        var creating = false;
        try
        {
            foreach (var path in message.Attachments)
            {
                var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                files.Add((path, stream));
                if (stream.Length > MaximumAttachmentBytes) throw new InvalidDataException($"Attachment '{Path.GetFileName(path)}' exceeds the Microsoft Graph 150 MB file limit.");
            }
            var payload = new
            {
                subject = message.Subject,
                body = new { contentType = string.IsNullOrWhiteSpace(message.Html) ? "Text" : "HTML", content = string.IsNullOrWhiteSpace(message.Html) ? message.Text : message.Html },
                toRecipients = Recipients(message.To), ccRecipients = Recipients(message.Cc), bccRecipients = Recipients(message.Bcc),
                internetMessageHeaders = new[] { new { name = "x-mergedesk-operation", value = operationId } }
            };
            // Acquire the token before marking the draft POST as potentially in flight.
            var accessToken = await getToken(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            creating = true;
            using (var response = await GraphRequestAsync(HttpMethod.Post, "me/messages", payload, cancellationToken, accessToken))
            {
                if (!response.IsSuccessStatusCode)
                {
                    // A definite client rejection is safe to report as failed. Server errors
                    // and request timeouts may follow a committed write: never retry blindly.
                    creating = (int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.RequestTimeout;
                    await EnsureSuccessAsync(response, cancellationToken);
                }
                using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
                draftId = body.RootElement.GetProperty("id").GetString();
                if (string.IsNullOrEmpty(draftId)) throw new InvalidDataException("Microsoft returned no draft identifier.");
            }
            creating = false;
            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (file.Stream.Length < SmallAttachmentBytes)
                    await AddSmallAttachmentAsync(draftId, file.Path, file.Stream, cancellationToken);
                else await UploadAttachmentAsync(draftId, file.Path, file.Stream, cancellationToken);
            }
            foreach (var image in message.InlineImages ?? [])
            {
                var inline = new Dictionary<string, object>
                {
                    ["@odata.type"] = "#microsoft.graph.fileAttachment", ["name"] = image.Name,
                    ["contentType"] = image.MediaType, ["contentBytes"] = Convert.ToBase64String(image.Bytes),
                    ["isInline"] = true, ["contentId"] = image.ContentId
                };
                using var response = await GraphRequestAsync(HttpMethod.Post, $"me/messages/{Uri.EscapeDataString(draftId)}/attachments", inline, cancellationToken);
                await EnsureSuccessAsync(response, cancellationToken);
            }
            return new(draftId, Destination);
        }
        catch (Exception ex) when (draftId != null || creating)
        {
            throw new DraftCreationException($"Draft creation was interrupted or incomplete. Check {Destination} before retrying. {ex.Message}", draftId, true, ex);
        }
        finally { foreach (var file in files) await file.Stream.DisposeAsync(); }
    }
    private static object[] Recipients(string value) => MergeEngine.SplitAddresses(value).Select(address => (object)new { emailAddress = new { address } }).ToArray();
    private async Task AddSmallAttachmentAsync(string id, string path, Stream stream, CancellationToken token)
    {
        using var buffer = new MemoryStream(); await stream.CopyToAsync(buffer, token);
        var payload = new Dictionary<string, object>
        {
            ["@odata.type"] = "#microsoft.graph.fileAttachment", ["name"] = Path.GetFileName(path),
            ["contentType"] = "application/octet-stream", ["contentBytes"] = Convert.ToBase64String(buffer.ToArray())
        };
        using var response = await GraphRequestAsync(HttpMethod.Post, $"me/messages/{Uri.EscapeDataString(id)}/attachments", payload, token);
        await EnsureSuccessAsync(response, token);
    }
    private async Task UploadAttachmentAsync(string id, string path, FileStream stream, CancellationToken token)
    {
        var payload = new { AttachmentItem = new { attachmentType = "file", name = Path.GetFileName(path), size = stream.Length, contentType = "application/octet-stream" } };
        using var session = await GraphRequestAsync(HttpMethod.Post, $"me/messages/{Uri.EscapeDataString(id)}/attachments/createUploadSession", payload, token);
        await EnsureSuccessAsync(session, token);
        using var sessionBody = JsonDocument.Parse(await session.Content.ReadAsStringAsync(token));
        var location = sessionBody.RootElement.GetProperty("uploadUrl").GetString();
        if (!Uri.TryCreate(location, UriKind.Absolute, out var uploadUri) || uploadUri.Scheme != "https") throw new InvalidDataException("Microsoft returned an invalid upload location.");
        var buffer = new byte[ChunkBytes];
        long position = 0;
        while (position < stream.Length)
        {
            var count = (int)Math.Min(buffer.Length, stream.Length - position);
            await stream.ReadExactlyAsync(buffer.AsMemory(0, count), token);
            using var request = new HttpRequestMessage(HttpMethod.Put, uploadUri) { Content = new ByteArrayContent(buffer, 0, count) };
            // The pre-authorized upload URL supplies its own authorization. Never attach
            // the Microsoft Graph bearer token to upload URLs or log the URL itself.
            request.Content.Headers.ContentType = new("application/octet-stream");
            request.Content.Headers.ContentRange = new(position, position + count - 1, stream.Length);
            using var response = await http.SendAsync(request, token);
            await EnsureSuccessAsync(response, token);
            position += count;
            if (position == stream.Length && response.StatusCode != HttpStatusCode.Created)
                throw new IOException("Microsoft did not confirm the completed attachment upload.");
        }
    }
    private async Task<HttpResponseMessage> GraphRequestAsync(HttpMethod method, string relative, object? payload, CancellationToken token, string? accessToken = null)
    {
        using var request = new HttpRequestMessage(method, GraphRoot + relative);
        request.Headers.Authorization = new("Bearer", accessToken ?? await getToken(token));
        request.Headers.TryAddWithoutValidation("Prefer", "IdType=\"ImmutableId\"");
        if (payload != null) request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        return await http.SendAsync(request, token);
    }
    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken token)
    {
        if (response.IsSuccessStatusCode) return;
        var text = await response.Content.ReadAsStringAsync(token);
        var detail = "";
        try
        {
            using var json = JsonDocument.Parse(text);
            detail = json.RootElement.GetProperty("error").GetProperty("message").GetString() ?? "";
            if (detail.Length > 500) detail = detail[..500];
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException) { }
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            var delay = response.Headers.RetryAfter?.Delta ?? (response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow);
            var retry = DateTimeOffset.UtcNow + (delay > TimeSpan.Zero ? delay.Value : TimeSpan.FromMinutes(1));
            throw new GraphRateLimitException($"Microsoft is throttling requests. Wait until {retry:O} before creating another batch; review existing drafts first.", retry);
        }
        throw new HttpRequestException($"Microsoft mailbox request failed ({(int)response.StatusCode}). {detail}", null, response.StatusCode);
    }
}
