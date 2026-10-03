using System.IO;
using System.Net;
using System.Net.Http;
using System.Windows.Threading;
using MergeDesk.App.Services;
using MergeDesk.App.ViewModels;
using MergeDesk.Core;
using MergeDesk.Infrastructure;

internal static class SendChecks
{
    private static int count;
    private static void Check(bool condition, string name) { if (!condition) throw new Exception("FAIL: " + name); count++; Console.WriteLine("PASS: " + name); }
    private static MergedMessage Message(int row = 2) => new(row, "a@example.com", "", "", "Hello", "<p>Hello</p>", "Hello", []);
    public static async Task<int> RunAsync(string root)
    {
        var ledgerPath = Path.Combine(root, "send-ledger.db"); var ledger = new SqliteSendLedger(ledgerPath);
        var sender = new Sender(); var results = new List<RunResult>();
        Task Report(RunResult r) { results.Add(r); return Task.CompletedTask; }
        await new LiveSendRunner(ledger).RunAsync([Message(), Message(3) with { To = "b@example.com" }], sender, "first", 0, Report);
        Check(sender.Created == 2 && sender.Submitted == 2 && results.All(r => r.Status == "Submitted to Outlook"), "live runner prepares then submits each message");
        results.Clear(); await new LiveSendRunner(new SqliteSendLedger(ledgerPath)).RunAsync([Message()], sender, "restart", 0, Report);
        Check(sender.Submitted == 2 && results.Single().Status == "Already submitted", "durable ledger skips identical submitted message after restart");
        var key = await LiveSendRunner.FingerprintAsync(Message(), sender.Destination);
        var reordered = await LiveSendRunner.FingerprintAsync(Message(99) with { Text = "ignored text alternative" }, "Microsoft mailbox / owner@example.com / Drafts");
        Check(key == reordered, "fingerprint ignores source row inactive text alternative and provider label");
        var attachment = Path.Combine(root, "send-fixture.txt"); await File.WriteAllTextAsync(attachment, "first bytes");
        var withFile = Message() with { Attachments = [attachment] };
        var oldKey = await LiveSendRunner.FingerprintAsync(withFile, sender.Destination);
        await File.WriteAllTextAsync(attachment, "different bytes");
        Check(oldKey != await LiveSendRunner.FingerprintAsync(withFile, sender.Destination), "attachment content participates in duplicate identity");
        var reservations = await Task.WhenAll(ledger.ReserveAsync("concurrent", "one"), ledger.ReserveAsync("concurrent", "two"));
        Check(reservations.Count(r => r.Owned) == 1, "atomic SQLite claim prevents concurrent duplicate operations");
        var uncertain = new Sender { Fail = "unknown" }; results.Clear();
        var unknownMessage = Message() with { Subject = "uncertain" };
        await new LiveSendRunner(ledger).RunAsync([unknownMessage, Message(3) with { Subject = "later" }], uncertain, "uncertain", 0, Report);
        Check(uncertain.Submitted == 1 && results[0].Status == "Needs review" && results[1].Status == "Not attempted", "uncertain submission stops the batch");
        results.Clear(); uncertain.Fail = "";
        await new LiveSendRunner(new SqliteSendLedger(ledgerPath)).RunAsync([unknownMessage], uncertain, "repeat-unknown", 0, Report);
        Check(uncertain.Submitted == 1 && results[0].Status == "Needs review", "uncertain submission remains blocked across restarts");
        var rejected = new Sender { Fail = "rejected" }; results.Clear();
        var rejectedMessage = Message() with { Subject = "rejected" };
        await new LiveSendRunner(ledger).RunAsync([rejectedMessage], rejected, "rejected", 0, Report);
        Check(results[0].Status == "Not sent" && rejected.Created == 1, "definite rejection preserves a prepared draft");
        rejected.Fail = ""; results.Clear();
        await new LiveSendRunner(ledger).RunAsync([rejectedMessage], rejected, "resume", 0, Report);
        Check(rejected.Created == 1 && rejected.Submitted == 2 && results[0].Status == "Submitted to Outlook", "confirmed rerun reuses a definitely unsent draft");
        var throttleMessage = Message() with { Subject = "throttle" }; var throttleKey = await LiveSendRunner.FingerprintAsync(throttleMessage, sender.Destination);
        var throttleClaim = await ledger.ReserveAsync(throttleKey, "throttle");
        await ledger.SaveAsync(throttleClaim.Attempt with { State = "Prepared", DraftId = "saved", RetryAfter = DateTimeOffset.UtcNow.AddMinutes(5) });
        results.Clear(); await new LiveSendRunner(ledger).RunAsync([throttleMessage], sender, "too-soon", 0, Report);
        Check(results[0].Status == "Needs review" && sender.Submitted == 2, "retry-after blocks submissions until restriction clears");
        using var cancellation = new CancellationTokenSource();
        var cancelling = new Sender { AfterSubmit = cancellation.Cancel }; results.Clear();
        await new LiveSendRunner(ledger).RunAsync([Message() with { Subject = "cancel-first" }, Message(3) with { Subject = "cancel-second" }], cancelling, "cancel", 1, Report, cancellation.Token);
        Check(results[0].Status == "Submitted to Outlook" && results[1].Status == "Cancelled" && cancelling.Submitted == 1, "cancellation during pacing preserves successful result and skips future sends");
        var prepareCancel = new Sender(); using var cts = new CancellationTokenSource(); prepareCancel.AfterCreate = cts.Cancel; results.Clear();
        var paused = Message() with { Subject = "prepared-cancel" };
        await new LiveSendRunner(ledger).RunAsync([paused], prepareCancel, "prepare-cancel", 0, Report, cts.Token);
        Check(prepareCancel.Submitted == 0 && results[0].Status == "Cancelled", "cancellation after preparation leaves unsent draft");
        prepareCancel.AfterCreate = null; results.Clear();
        await new LiveSendRunner(ledger).RunAsync([paused], prepareCancel, "resume-prepared", 0, Report);
        Check(prepareCancel.Created == 1 && prepareCancel.Submitted == 1, "prepared cancellation resumes without creating another draft");
        var crashMessage = Message() with { Subject = "crash" }; var crashKey = await LiveSendRunner.FingerprintAsync(crashMessage, sender.Destination);
        var crash = await ledger.ReserveAsync(crashKey, "crash-before-send"); await ledger.SaveAsync(crash.Attempt with { State = "Submitting", DraftId = "crashed" });
        results.Clear(); await new LiveSendRunner(ledger).RunAsync([crashMessage], sender, "restart-crash", 0, Report);
        Check(results[0].Status == "Needs review", "persisted submitting marker blocks blind recovery after a crash");
        var blockedSender = new Sender(); results.Clear();
        await new LiveSendRunner(new RejectSubmittingLedger()).RunAsync([Message()], blockedSender, "log-block", 0, Report);
        Check(blockedSender.Created == 1 && blockedSender.Submitted == 0 && results[0].Status == "Needs review", "failure to persist submitting state prevents any send call");
        await GraphChecks(root);
        return count;
    }
    private static async Task GraphChecks(string root)
    {
        foreach (var status in new[] { HttpStatusCode.Accepted, HttpStatusCode.Unauthorized, HttpStatusCode.TooManyRequests, HttpStatusCode.ServiceUnavailable })
        {
            var handler = new Handler(status); var http = new HttpClient(handler);
            var drafts = new GraphDraftCreator(http, _ => Task.FromResult("fixture"), "owner@example.com");
            var graph = new GraphMailSender(drafts, http, _ => Task.FromResult("fixture"));
            try
            {
                await graph.SubmitDraftAsync("draft/1");
                Check(status == HttpStatusCode.Accepted, "Graph 202 means accepted submission");
            }
            catch (SubmissionRejectedException ex) { Check(status is HttpStatusCode.Unauthorized or HttpStatusCode.TooManyRequests && (status != HttpStatusCode.TooManyRequests || ex.RetryAfter > DateTimeOffset.UtcNow), $"Graph {(int)status} rejection is definite and retains retry restriction"); }
            catch (IOException) { Check(status == HttpStatusCode.ServiceUnavailable, "Graph server error is an uncertain send outcome"); }
            Check(handler.Calls == 1 && handler.Url.EndsWith("draft%2F1/send") && handler.Method == "POST" && handler.Auth == "Bearer fixture", $"Graph {(int)status} submission uses escaped ID delegated auth and no blind retry");
        }
        var never = new Handler(HttpStatusCode.Accepted); var client = new HttpClient(never);
        var noAuth = new GraphMailSender(new GraphDraftCreator(client, _ => Task.FromResult("fixture"), "owner@example.com"), client, _ => throw new InvalidOperationException("expired session"));
        try { await noAuth.SubmitDraftAsync("draft"); throw new Exception("expected error"); }
        catch (SubmissionRejectedException) { Check(never.Calls == 0, "authentication failure cannot submit a message"); }
        var throttle = new Handler(HttpStatusCode.TooManyRequests); var throttleHttp = new HttpClient(throttle);
        var rateSender = new GraphMailSender(new GraphDraftCreator(throttleHttp, _ => Task.FromResult("fixture"), "owner@example.com"), throttleHttp, _ => Task.FromResult("fixture"));
        var ledger = new SqliteSendLedger(Path.Combine(root, "draft-throttle-ledger.db")); var results = new List<RunResult>();
        Task Report(RunResult r) { results.Add(r); return Task.CompletedTask; }
        await new LiveSendRunner(ledger).RunAsync([Message()], rateSender, "rate-create", 0, Report);
        Check(throttle.Calls == 1 && results[0].Status == "Not sent" && results[0].DraftId == null, "throttled draft creation stops before sending");
        results.Clear(); await new LiveSendRunner(ledger).RunAsync([Message()], rateSender, "rate-retry", 0, Report);
        Check(throttle.Calls == 1 && results[0].Status == "Needs review", "draft creation Retry-After prevents immediate repeat requests");
    }
    public static int Workflow(string root)
    {
        var before = count;
        var path = Path.Combine(root, "send-workflow.csv"); File.WriteAllText(path, "Email\na@example.com\n");
        var connector = new Connector(); var dialogs = new Dialogs(path); var ledger = new SqliteSendLedger(Path.Combine(root, "workflow-send-ledger.db"));
        var model = new MainViewModel(new RecipientReader(), new SqliteRunRepository(Path.Combine(root, "workflow-send-history.db")), dialogs, connector,
            draftLogRoot: Path.Combine(root, "workflow-send-runs"), sendLedger: ledger);
        void Execute(AsyncCommand command)
        {
            command.Execute(null); var limit = DateTime.UtcNow.AddSeconds(20);
            while (!command.CanExecute(null) && DateTime.UtcNow < limit) { Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); Thread.Sleep(5); }
            if (DateTime.UtcNow >= limit) throw new TimeoutException("send workflow did not finish");
        }
        Execute(model.ImportCommand); Execute(model.ConnectClassicCommand); Execute(model.ValidateCommand);
        Check(model.Issues.Count == 0 && !model.Html.Contains("{{") && !model.Subject.Contains("{{"), "generic template accepts Email-only source without account or name columns");
        Execute(model.SendCommand);
        Check(dialogs.Confirmations == 0 && connector.Provider.Submitted == 0 && model.Issues.Any(i => i.Area == "Body"), "starter instructions must be replaced before sending");
        model.Html = "<p>Hello from our team.</p>"; dialogs.Confirm = false; Execute(model.SendCommand);
        Check(dialogs.Confirmations == 1 && connector.Provider.Created == 0, "declined live confirmation prevents all mailbox changes");
        dialogs.Confirm = true; model.SendIntervalSeconds = 1; Execute(model.SendCommand);
        Check(connector.Provider.Submitted == 1 && model.History[0].Mode == "Live send" && model.Results[0].Status == "Submitted to Outlook", "confirmed live UI flow submits and logs result");
        Execute(model.SendCommand);
        Check(connector.Provider.Submitted == 1 && model.Results[0].Status == "Already submitted", "repeat live UI flow skips previously submitted message");
        model.CloseConnection(); return count - before;
    }
    private sealed class Sender : IMailDelivery
    {
        public int Created, Submitted; public string Fail = ""; public Action? AfterCreate, AfterSubmit;
        public string Destination => "Classic Outlook / owner@example.com / Drafts";
        public string SubmissionStatus => "Submitted to Outlook";
        public Task<DraftReceipt> CreateDraftAsync(MergedMessage message, string operationId, CancellationToken cancellationToken = default)
        { Created++; AfterCreate?.Invoke(); return Task.FromResult(new DraftReceipt("draft-" + Created, Destination)); }
        public Task SubmitDraftAsync(string id, CancellationToken token = default)
        { Submitted++; if (Fail == "unknown") throw new IOException("lost response"); if (Fail == "rejected") throw new SubmissionRejectedException("definite rejection"); AfterSubmit?.Invoke(); return Task.CompletedTask; }
        public Task<string> SendAsync(MergedMessage message, CancellationToken cancellationToken = default) => throw new NotSupportedException("tests use staged interface");
    }
    private sealed class RejectSubmittingLedger : ISendLedger
    {
        public Task<DeliveryReservation> ReserveAsync(string key, string operationId) => Task.FromResult(new DeliveryReservation(new(key, operationId, "Drafting"), true));
        public Task SaveAsync(DeliveryAttempt attempt) => attempt.State == "Submitting" ? Task.FromException(new IOException("fixture log failure")) : Task.CompletedTask;
    }
    private sealed class Handler(HttpStatusCode status) : HttpMessageHandler
    {
        public int Calls; public string Url = "", Method = "", Auth = "";
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { Calls++; Url = request.RequestUri!.AbsoluteUri; Method = request.Method.Method; Auth = request.Headers.Authorization!.ToString(); var response = new HttpResponseMessage(status); if (status == HttpStatusCode.TooManyRequests) response.Headers.RetryAfter = new(TimeSpan.FromMinutes(5)); return Task.FromResult(response); }
    }
    private sealed class Connector : IDraftConnector
    {
        public Sender Provider { get; } = new();
        public Task<IReadOnlyList<DraftAccount>> ConnectClassicAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<DraftAccount>>([new("store", "Owner", "owner@example.com")]);
        public IMailDraftCreator UseClassicAccount(DraftAccount account) => Provider;
        public Task<IMailDraftCreator> ConnectMicrosoftAsync(MicrosoftConnectionSettings settings, CancellationToken token) => Task.FromResult<IMailDraftCreator>(Provider);
        public void Disconnect() { } public void Dispose() { }
    }
    private sealed class Dialogs(string path) : IDesktopDialogs
    {
        public bool Confirm; public int Confirmations;
        public bool ConfirmSending(int count, string destination, int intervalSeconds) { Confirmations++; return Confirm; }
        public string? OpenSource() => path; public string[] OpenAttachments() => []; public string? ChooseOutput() => null; public string? ProjectFile(bool save) => null;
    }
}
