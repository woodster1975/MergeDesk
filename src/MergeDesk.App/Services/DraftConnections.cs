using System.IO;
using System.Net.Http;
using System.Text.Json;
using MergeDesk.Core;
using MergeDesk.Infrastructure;
using Microsoft.Identity.Client;

namespace MergeDesk.App.Services;

public sealed record MicrosoftConnectionSettings(string ClientId = "", string Tenant = "common", bool EnableSending = false);
public interface IDraftConnector : IDisposable
{
    Task<IReadOnlyList<DraftAccount>> ConnectClassicAsync(CancellationToken token);
    IMailDraftCreator UseClassicAccount(DraftAccount account);
    Task<IMailDraftCreator> ConnectMicrosoftAsync(MicrosoftConnectionSettings settings, CancellationToken token);
    void Disconnect();
}
public interface IConnectionSettingsStore
{
    Task<MicrosoftConnectionSettings> LoadAsync();
    Task SaveAsync(MicrosoftConnectionSettings settings);
}
public sealed class JsonConnectionSettingsStore(string path) : IConnectionSettingsStore
{
    public async Task<MicrosoftConnectionSettings> LoadAsync() => File.Exists(path)
        ? JsonSerializer.Deserialize<MicrosoftConnectionSettings>(await File.ReadAllTextAsync(path)) ?? new() : new();
    public async Task SaveAsync(MicrosoftConnectionSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
    }
}
public sealed class OutlookDraftConnector : IDraftConnector
{
    private ClassicOutlookService? classic;
    private IPublicClientApplication? identity;
    private readonly HttpClient http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromMinutes(5) };
    public Task<IReadOnlyList<DraftAccount>> ConnectClassicAsync(CancellationToken token)
    { classic ??= new ClassicOutlookService(); return classic.GetAccountsAsync(token); }
    public IMailDraftCreator UseClassicAccount(DraftAccount account)
    { if (classic == null) throw new InvalidOperationException("Connect to classic Outlook first."); return classic.ForAccount(account); }
    public async Task<IMailDraftCreator> ConnectMicrosoftAsync(MicrosoftConnectionSettings settings, CancellationToken token)
    {
        if (!Guid.TryParse(settings.ClientId.Trim(), out var clientId) || clientId == Guid.Empty)
            throw new InvalidDataException("Enter the Application (client) ID from your Microsoft app registration. See the README setup instructions.");
        var tenant = settings.Tenant.Trim();
        string[] scopes = settings.EnableSending ? ["Mail.ReadWrite", "Mail.Send"] : ["Mail.ReadWrite"];
        if (tenant is not ("common" or "organizations" or "consumers") && !Guid.TryParse(tenant, out _))
            throw new InvalidDataException("Tenant must be common, organizations, consumers, or a tenant ID (GUID).");
        var client = PublicClientApplicationBuilder.Create(clientId.ToString())
            .WithAuthority($"https://login.microsoftonline.com/{tenant}").WithRedirectUri("http://localhost").Build();
        // Public desktop client: browser sign-in, no password handling or client secret.
        var result = await client.AcquireTokenInteractive(scopes).WithUseEmbeddedWebView(false)
            .WithPrompt(Prompt.SelectAccount).ExecuteAsync(token);
        var account = result.Account ?? throw new InvalidOperationException("Microsoft sign-in returned no account.");
        async Task<string> GetToken(CancellationToken cancellationToken)
        {
            try { return (await client.AcquireTokenSilent(scopes, account).ExecuteAsync(cancellationToken)).AccessToken; }
            catch (MsalUiRequiredException) { throw new InvalidOperationException("The Microsoft session requires sign-in again. Reconnect before creating more drafts."); }
        }
        var creator = new GraphDraftCreator(http, GetToken, account.Username);
        await creator.ValidateConnectionAsync(token);
        identity = client; // MSAL's in-memory cache only. Nothing persists tokens to disk.
        return settings.EnableSending ? new GraphMailSender(creator, http, GetToken) : creator;
    }
    public void Disconnect() { identity = null; classic?.Dispose(); classic = null; }
    public void Dispose() { Disconnect(); http.Dispose(); }
}
