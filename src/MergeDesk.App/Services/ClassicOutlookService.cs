using System.Runtime.InteropServices;
using System.Windows.Threading;
using MergeDesk.Core;

namespace MergeDesk.App.Services;

/// <summary>Late-bound classic Outlook automation isolated on a dedicated STA with a message pump.</summary>
public sealed class ClassicOutlookService : IDisposable
{
    private readonly Dispatcher dispatcher;
    private object? application;
    private bool disposed;
    public ClassicOutlookService()
    {
        var ready = new TaskCompletionSource<Dispatcher>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => { ready.SetResult(Dispatcher.CurrentDispatcher); Dispatcher.Run(); }) { IsBackground = true, Name = "MergeDesk Outlook STA" };
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); dispatcher = ready.Task.GetAwaiter().GetResult();
    }
    public async Task<IReadOnlyList<DraftAccount>> GetAccountsAsync(CancellationToken token) => await dispatcher.InvokeAsync(ReadAccounts, DispatcherPriority.Normal, token).Task;
    public IMailDraftCreator ForAccount(DraftAccount account) => new Creator(this, account);
    private dynamic Application
    {
        get
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (application == null)
            {
                var type = Type.GetTypeFromProgID("Outlook.Application") ?? throw new InvalidOperationException("Classic Outlook is not installed or registered. Choose the Microsoft mailbox connection for new Outlook.");
                application = Activator.CreateInstance(type) ?? throw new InvalidOperationException("Could not start classic Outlook.");
            }
            return application;
        }
    }
    private IReadOnlyList<DraftAccount> ReadAccounts()
    {
        object? session = null, accounts = null;
        try
        {
            session = Application.Session; accounts = ((dynamic)session).Accounts;
            var result = new List<DraftAccount>();
            for (var index = 1; index <= (int)((dynamic)accounts).Count; index++)
            {
                object? account = null, store = null;
                try
                {
                    account = ((dynamic)accounts).Item(index); store = ((dynamic)account).DeliveryStore;
                    var email = (string)((dynamic)account).SmtpAddress;
                    if (store != null && !string.IsNullOrWhiteSpace(email))
                        result.Add(new((string)((dynamic)store).StoreID, (string)((dynamic)account).DisplayName, email));
                }
                finally { Release(store); Release(account); }
            }
            return result;
        }
        finally { Release(accounts); Release(session); }
    }
    private async Task<DraftReceipt> CreateAsync(DraftAccount selected, MergedMessage message, string operationId, CancellationToken token)
    {
        return await dispatcher.InvokeAsync(() => CreateOnSta(selected, message, operationId, token), DispatcherPriority.Normal, token).Task;
    }
    private DraftReceipt CreateOnSta(DraftAccount selected, MergedMessage message, string operationId, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        object? session = null, accounts = null, account = null, store = null, drafts = null, items = null, mail = null, recipients = null, attachments = null, properties = null, marker = null;
        var saving = false; string? id = null;
        string? imageFolder = null;
        try
        {
            session = Application.Session; accounts = ((dynamic)session).Accounts;
            for (var index = 1; index <= (int)((dynamic)accounts).Count; index++)
            {
                account = ((dynamic)accounts).Item(index); store = ((dynamic)account).DeliveryStore;
                if (store != null && (string)((dynamic)store).StoreID == selected.Key && string.Equals((string)((dynamic)account).SmtpAddress, selected.Email, StringComparison.OrdinalIgnoreCase)) break;
                Release(store); store = null; Release(account); account = null;
            }
            if (account == null || store == null) throw new InvalidOperationException("The selected Outlook account is no longer available. Reconnect and select an account.");
            drafts = ((dynamic)store).GetDefaultFolder(16); // olFolderDrafts, in the selected account's store.
            items = ((dynamic)drafts).Items; mail = ((dynamic)items).Add("IPM.Note");
            ((dynamic)mail).SendUsingAccount = account;
            ((dynamic)mail).Subject = message.Subject;
            if (!string.IsNullOrWhiteSpace(message.Html)) { ((dynamic)mail).BodyFormat = 2; ((dynamic)mail).HTMLBody = message.Html; }
            else { ((dynamic)mail).BodyFormat = 1; ((dynamic)mail).Body = message.Text; }
            recipients = ((dynamic)mail).Recipients;
            AddRecipients(recipients, message.To, 1); AddRecipients(recipients, message.Cc, 2); AddRecipients(recipients, message.Bcc, 3);
            if (!(bool)((dynamic)recipients).ResolveAll()) throw new InvalidOperationException("Outlook could not resolve one or more recipient addresses.");
            attachments = ((dynamic)mail).Attachments;
            foreach (var path in message.Attachments)
            {
                token.ThrowIfCancellationRequested();
                object? attachment = null;
                try { attachment = ((dynamic)attachments).Add(path, 1); }
                finally { Release(attachment); }
            }
            foreach (var image in message.InlineImages ?? [])
            {
                token.ThrowIfCancellationRequested();
                imageFolder ??= System.IO.Path.Combine(System.IO.Path.GetTempPath(), "MergeDesk-inline-" + Guid.NewGuid().ToString("N"));
                System.IO.Directory.CreateDirectory(imageFolder);
                var path = System.IO.Path.Combine(imageFolder, image.Name); System.IO.File.WriteAllBytes(path, image.Bytes);
                object? attachment = null, accessor = null;
                try
                {
                    attachment = ((dynamic)attachments).Add(path, 1); accessor = ((dynamic)attachment).PropertyAccessor;
                    ((dynamic)accessor).SetProperty("http://schemas.microsoft.com/mapi/proptag/0x3712001F", image.ContentId);
                    ((dynamic)accessor).SetProperty("http://schemas.microsoft.com/mapi/proptag/0x370E001F", image.MediaType);
                    ((dynamic)accessor).SetProperty("http://schemas.microsoft.com/mapi/proptag/0x7FFE000B", true);
                }
                finally { Release(accessor); Release(attachment); }
            }
            properties = ((dynamic)mail).UserProperties; marker = ((dynamic)properties).Add("MergeDeskOperation", 1, false); ((dynamic)marker).Value = operationId;
            token.ThrowIfCancellationRequested(); saving = true;
            ((dynamic)mail).Save(); // Deliberately no Send, Display, or Outlook Quit call.
            id = (string)((dynamic)mail).EntryID;
            if (string.IsNullOrWhiteSpace(id)) throw new InvalidOperationException("Outlook returned no saved draft identifier.");
            return new(id, $"Classic Outlook / {selected.Email} / Drafts");
        }
        catch (Exception ex) when (saving)
        { throw new DraftCreationException($"Outlook may have saved a draft. Check Drafts in {selected.Email} before retrying. {ex.Message}", id, true, ex); }
        finally
        {
            Release(marker); Release(properties); Release(attachments); Release(recipients); Release(mail);
            Release(items); Release(drafts); Release(store); Release(account); Release(accounts); Release(session);
            if (imageFolder != null)
            {
                // Only remove files created in this operation's unique temporary directory.
                try { foreach (var path in System.IO.Directory.GetFiles(imageFolder)) System.IO.File.Delete(path); System.IO.Directory.Delete(imageFolder); }
                catch (System.IO.IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
    private static void AddRecipients(dynamic recipients, string addresses, int type)
    {
        foreach (var address in MergeEngine.SplitAddresses(addresses))
        {
            object? recipient = null;
            try { recipient = recipients.Add(address); ((dynamic)recipient).Type = type; }
            finally { Release(recipient); }
        }
    }
    private static void Release(object? value) { if (value != null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }
    public void Dispose()
    {
        if (disposed) return;
        dispatcher.Invoke(() => { Release(application); application = null; disposed = true; });
        dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
    }
    private async Task SubmitAsync(DraftAccount selected, string id, CancellationToken token)
    {
        await dispatcher.InvokeAsync(() =>
        {
            token.ThrowIfCancellationRequested();
            object? session = null, mail = null, folder = null, store = null, drafts = null, sendAccount = null;
            try
            {
                session = Application.Session; mail = ((dynamic)session).GetItemFromID(id, selected.Key);
                folder = ((dynamic)mail).Parent;
                store = ((dynamic)session).GetStoreFromID(selected.Key); drafts = ((dynamic)store).GetDefaultFolder(16);
                if ((bool)((dynamic)mail).Sent || (string)((dynamic)folder).StoreID != selected.Key || (string)((dynamic)folder).EntryID != (string)((dynamic)drafts).EntryID)
                    throw new SubmissionRejectedException("Saved message is no longer in this account's Drafts folder. Check Outlook before retrying.");
                // Outlook can return a null/default SendUsingAccount when reopening a draft.
                // Resolve the user's selection from the live profile instead, and explicitly
                // assign it immediately before Send; never fall back to the default account.
                sendAccount = ResolveSendingAccount(session, selected);
                ((dynamic)mail).SendUsingAccount = sendAccount;
                token.ThrowIfCancellationRequested();
                ((dynamic)mail).Send();
            }
            finally { Release(sendAccount); Release(drafts); Release(store); Release(folder); Release(mail); Release(session); }
        }, DispatcherPriority.Normal, token).Task;
    }
    internal static object ResolveSendingAccount(object session, DraftAccount selected)
    {
        object? accounts = null;
        try
        {
            accounts = ((dynamic)session).Accounts;
            for (var index = 1; index <= (int)((dynamic)accounts).Count; index++)
            {
                object? account = null, store = null;
                var matched = false;
                try
                {
                    account = ((dynamic)accounts).Item(index); store = ((dynamic)account).DeliveryStore;
                    matched = store != null && (string)((dynamic)store).StoreID == selected.Key &&
                        string.Equals(((string)((dynamic)account).SmtpAddress).Trim(), selected.Email.Trim(), StringComparison.OrdinalIgnoreCase);
                    if (matched) return account!;
                }
                finally { Release(store); if (!matched) Release(account); }
            }
            throw new SubmissionRejectedException("The selected account is no longer in the Outlook profile. Reconnect and select the intended account before retrying.");
        }
        finally { Release(accounts); }
    }
    private sealed class Creator(ClassicOutlookService service, DraftAccount account) : IMailDelivery
    {
        public string Destination => $"Classic Outlook / {account.Email} / Drafts";
        public string SubmissionStatus => "Submitted to Outlook";
        public Task<DraftReceipt> CreateDraftAsync(MergedMessage message, string operationId, CancellationToken cancellationToken = default) => service.CreateAsync(account, message, operationId, cancellationToken);
        public Task SubmitDraftAsync(string draftId, CancellationToken token = default) => service.SubmitAsync(account, draftId, token);
        public async Task<string> SendAsync(MergedMessage message, CancellationToken cancellationToken = default)
        { var draft = await CreateDraftAsync(message, Guid.NewGuid().ToString("N"), cancellationToken); await SubmitDraftAsync(draft.Id, cancellationToken); return draft.Id; }
    }
}
