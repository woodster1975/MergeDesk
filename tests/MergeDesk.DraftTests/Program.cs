using System.IO;

using System.Net;

using System.Net.Http;

using System.Text;

using System.Text.Json;

using System.Windows.Threading;

using MergeDesk.Core;

using MergeDesk.Infrastructure;

using MergeDesk.App.Services;

using MergeDesk.App.ViewModels;



internal static class Program

{

    private static int passed;

    [STAThread]

    private static int Main(string[] args)

    {

        if (args.Contains("--workspace"))

        {

            try

            {

                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());

                var app = new MergeDesk.App.App(); app.InitializeComponent();

                var workspaceRoot = Path.Combine(Path.GetTempPath(), "MergeDesk-Workspace-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(workspaceRoot);

                Console.WriteLine($"{WorkspaceChecks.Run(workspaceRoot)} workspace checks passed."); return 0;

            }

            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }

        }

        if (args.Contains("--help")) { try { HelpCapture(args.Last()); return 0; } catch (Exception ex) { Console.Error.WriteLine(ex); return 1; } }

        if (args.Contains("--account"))

        { try { AccountChecks.Run(); return 0; } catch (Exception ex) { Console.Error.WriteLine(ex); return 1; } }

        if (args.Contains("--layout"))

        {

            try

            {

                var layoutApp = new MergeDesk.App.App(); layoutApp.InitializeComponent();

                var layoutWindow = new MergeDesk.App.MainWindow(); LayoutCheck(layoutWindow); layoutWindow.Close();

                Console.WriteLine("Validation layout check passed."); return 0;

            }

            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }

        }

        var root = Path.Combine(Path.GetTempPath(), "MergeDesk-Drafts-" + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(root);

        try

        {

            NetworkChecks(root).GetAwaiter().GetResult();

            BatchChecks().GetAwaiter().GetResult();

            JournalChecks(root).GetAwaiter().GetResult();

            passed += SendChecks.RunAsync(root).GetAwaiter().GetResult();

            passed += ImageChecks.RunAsync(root).GetAwaiter().GetResult();

            passed += AccountChecks.Run();

            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());

            var app = new MergeDesk.App.App(); app.InitializeComponent();

            var window = new MergeDesk.App.MainWindow();

            Check(window.DataContext is MainViewModel, "complete WPF window constructs with resources and new connection tab");

            passed += ImageChecks.UI(window);

            LayoutCheck(window);

            window.Close();

            WorkflowChecks(root);

            passed += SendChecks.Workflow(root);

            passed += WorkspaceChecks.Run(root);
            passed += LibraryChecks.Run(root);
            passed += HistoryChecks.Run(root);

            Console.WriteLine($"{passed} draft checks passed."); return 0;

        }

        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }

    }



    private static void HelpCapture(string destination)

    {

        Directory.CreateDirectory(destination);

        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());

        var app = new MergeDesk.App.App(); app.InitializeComponent();

        var window = new MergeDesk.App.MainWindow();

        var original = (MainViewModel)window.DataContext;

        window.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,

            new System.Windows.Interop.HwndSource(new System.Windows.Interop.HwndSourceParameters("Help key fixture") { Width = 1, Height = 1 }), 0, System.Windows.Input.Key.F1)

            { RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent });

        Check(original.SelectedTab == 5, "F1 opens Help without connecting or sending");

        var source = Path.Combine(destination, "demo.csv");

        File.WriteAllText(source, "Email,FirstName,Reference\nalex@example.com,Alex,REF-001\njordan@example.com,Jordan,REF-002\nsam@example.com,Sam,REF-003\n");

        var model = new MainViewModel(new RecipientReader(), new FailingRepository(), new Dialogs(source), new Connector(), draftLogRoot: destination);

        window.DataContext = model;

        Execute(model.ImportCommand);

        model.Subject = "Your update, {{FirstName}}";

        model.Html = "<p>Hello {{FirstName}},</p><p>Thank you for being part of our community.</p><p><strong>Here is your latest update.</strong></p><p>Kind regards,<br>The team</p>";

        model.Text = "Hello {{FirstName}},\n\nThank you for being part of our community.\nHere is your latest update.\n\nKind regards,\nThe team";

        model.RecipientView!.Table!.Rows[2][model.InclusionColumn] = false; model.TestAddress = "reviewer@example.com";
        Execute(model.ValidateCommand); Execute(model.PreviewCommand);

        ((System.Windows.Controls.RichTextBox)window.FindName("HtmlPreview")).Document = SafeHtmlPreview.Render(model.PreviewHtml);

        var root = (System.Windows.Controls.Grid)window.Content;

        window.Content = null; root.DataContext = model; root.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(243,246,248));

        System.Windows.Documents.TextElement.SetFontFamily(root, new System.Windows.Media.FontFamily("Segoe UI"));

        System.Windows.Documents.TextElement.SetFontSize(root, 14);

        var tabs = root.Children.OfType<System.Windows.Controls.TabControl>().Single();

        void Capture(string name, int width = 1440, int height = 940)

        {

            root.Measure(new System.Windows.Size(width,height)); root.Arrange(new System.Windows.Rect(0,0,width,height)); root.UpdateLayout();

            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

            root.Measure(new System.Windows.Size(width,height)); root.Arrange(new System.Windows.Rect(0,0,width,height)); root.UpdateLayout();

            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(width,height,96,96,System.Windows.Media.PixelFormats.Pbgra32);

            bitmap.Render(root);

            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));

            using var output = File.Create(Path.Combine(destination, name+".png")); encoder.Save(output);

        }

        for (var i=0; i<6; i++) { model.SelectedTab=i; Capture($"screen-{i}"); }

        model.SelectedTab=1; Capture("compose-top");
        var libraryPanel=(System.Windows.Controls.Expander)window.FindName("LibraryPanel"); libraryPanel.IsExpanded=true; Capture("templates"); Capture("templates-min",920,650); libraryPanel.IsExpanded=false;
        var emailEditor=(MergeDesk.App.Controls.RichEmailEditor)window.FindName("EmailEditor");
        ((System.Windows.Controls.Expander)emailEditor.FindName("PictureSettings")).IsExpanded=true;
        var editorScroll=(System.Windows.Controls.ScrollViewer)((System.Windows.Controls.TabItem)tabs.Items[1]).Content;
        editorScroll.ScrollToVerticalOffset(460); Capture("picture-settings"); editorScroll.ScrollToTop();
        ((System.Windows.Controls.Expander)emailEditor.FindName("PictureSettings")).IsExpanded=false;

        var composeScroll=(System.Windows.Controls.ScrollViewer)((System.Windows.Controls.TabItem)tabs.Items[1]).Content;

        composeScroll.ScrollToVerticalOffset(320); Capture("compose-bottom");

        model.SelectedTab=3; model.SelectedConnection=1; Capture("microsoft");

        model.SelectedTab=2;

        System.Windows.Controls.TabControl? FindNested(System.Windows.DependencyObject parent)

        {

            for(var n=0;n<System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);n++)

            { var child=System.Windows.Media.VisualTreeHelper.GetChild(parent,n); if(child is System.Windows.Controls.TabControl t) return t; var found=FindNested(child); if(found!=null)return found; }

            return null;

        }

        Capture("preview-details");

        FindNested((System.Windows.DependencyObject)((System.Windows.Controls.TabItem)tabs.Items[2]).Content)!.SelectedIndex=1;

        Capture("preview-email");

        model.SelectedTab=0; Capture("recipients-min", 920,650);
        model.SelectedTab=4; Capture("results-min",920,650);
        var recoveryModel = new MainViewModel(new RecipientReader(), new FailingRepository(), new Dialogs(source),
            recoveryStore:new ScreenshotRecovery(model.ProjectSnapshot()), draftLogRoot:destination);
        Wait(recoveryModel.LoadRecoveryAsync()); root.DataContext=recoveryModel; Capture("recovery");
        Capture("recovery-min",920,650); root.DataContext=model;
        var help = (MergeDesk.App.Controls.HelpView)((System.Windows.Controls.TabItem)tabs.Items[5]).Content;

        var search=(System.Windows.Controls.TextBox)help.FindName("Search");

        var topics=(System.Windows.Controls.ListBox)help.FindName("Topics");

        search.Text="attachment"; Check(topics.Items.Count>0 && topics.Items.Count<14, "help searches instruction text as well as titles");

        search.Text="unlikely-to-match-123"; Check(topics.Items.Count==0 && ((System.Windows.Controls.TextBlock)help.FindName("TopicTitle")).Text=="No matching topics", "help search handles no matches");

        search.Text=""; Check(topics.Items.Count==14 && topics.SelectedIndex==0, "clearing search restores all topics");

        window.Close();

        Console.WriteLine("Help checks and six app screenshots completed; no live mailbox was accessed.");

    }



    private sealed class ScreenshotRecovery(MergeProject project) : IProjectRecoveryStore
    {
        public Task<RecoverySnapshot?> LoadAsync() => Task.FromResult<RecoverySnapshot?>(new(DateTimeOffset.Parse("2026-10-03T09:30:00Z"),project));
        public Task SaveAsync(MergeProject snapshot)=>Task.CompletedTask;
        public Task AcknowledgeAsync()=>Task.CompletedTask;
    }
    private static void Check(bool value, string name)

    { if (!value) throw new Exception("FAIL: " + name); passed++; Console.WriteLine("PASS: " + name); }

    private static void LayoutCheck(MergeDesk.App.MainWindow window)

    {

        var grid = (System.Windows.Controls.DataGrid)window.FindName("ValidationIssues");

        ((System.Windows.Controls.Grid)grid.Parent).Children.Remove(grid);

        grid.FontSize = 14; grid.FontFamily = new System.Windows.Media.FontFamily("Segoe UI");

        grid.ItemsSource = new[] { new ValidationIssue(0, "Body", "Replace the starter example text and signature before sending.") };

        grid.SelectedIndex = 0;

        var host = new System.Windows.Controls.Grid(); host.Children.Add(grid);

        void Layout(double width)

        {

            host.Measure(new System.Windows.Size(width, 250)); host.Arrange(new System.Windows.Rect(0, 0, width, 250)); host.UpdateLayout();

            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

            host.Measure(new System.Windows.Size(width, 250)); host.Arrange(new System.Windows.Rect(0, 0, width, 250)); host.UpdateLayout();

        }

        Layout(390);

        var row = (System.Windows.Controls.DataGridRow)grid.ItemContainerGenerator.ContainerFromIndex(0);

        var text = (System.Windows.Controls.TextBlock)grid.Columns[2].GetCellContent(row);

        var wideHeight = row.ActualHeight;

        Layout(300);

        Check(wideHeight > 36 && row.ActualHeight >= wideHeight && row.ActualHeight >= text.DesiredSize.Height && Equals(text.ToolTip, text.Text),

            "selected validation row grows for wrapped text at narrow widths without clipping");

    }

    private static MergedMessage Message(int row = 2, params string[] attachments) => new(row, "alex@example.com;bob@example.com", "cc@example.com", "bcc@example.com", "Statement 00123", "<p><strong>Hello Alex</strong></p>", "Hello Alex", attachments);

    private static HttpResponseMessage Reply(HttpStatusCode status, string json) => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static GraphDraftCreator Graph(Handler handler) => new(new HttpClient(handler), _ => Task.FromResult("fixture-token"), "owner@example.com");

    private static async Task NetworkChecks(string root)

    {

        var file = Path.Combine(root, "statement.pdf"); await File.WriteAllTextAsync(file, "fixture bytes");

        var handler = new Handler((r, n) => Task.FromResult(Reply(n == 0 ? HttpStatusCode.Created : HttpStatusCode.Created, n == 0 ? "{\"id\":\"draft/1\"}" : "{\"id\":\"attachment\"}")));

        var receipt = await Graph(handler).CreateDraftAsync(Message(2, file), "batch-row-2");

        Check(receipt.Id == "draft/1" && handler.Requests.Count == 2, "draft and attachment created once");

        using var payload = JsonDocument.Parse(handler.Requests[0].Body);

        var body = payload.RootElement;

        Check(body.GetProperty("toRecipients").GetArrayLength() == 2 && body.GetProperty("ccRecipients").GetArrayLength() == 1 && body.GetProperty("bccRecipients").GetArrayLength() == 1, "To CC BCC retained");

        Check(body.GetProperty("subject").GetString() == "Statement 00123" && body.GetProperty("body").GetProperty("content").GetString() == Message().Html && body.GetProperty("body").GetProperty("contentType").GetString() == "HTML", "subject and rich HTML retained");

        Check(body.GetProperty("internetMessageHeaders")[0].GetProperty("value").GetString() == "batch-row-2", "operation identifier attached to draft");

        using var attachment = JsonDocument.Parse(handler.Requests[1].Body);

        Check(Encoding.UTF8.GetString(Convert.FromBase64String(attachment.RootElement.GetProperty("contentBytes").GetString()!)) == "fixture bytes", "attachment bytes embedded");

        Check(handler.Requests[1].Url.Contains("draft%2F1/attachments") && handler.Requests.All(r => r.Auth == "Bearer fixture-token" && r.Prefer.Contains("ImmutableId")), "escaped immutable IDs and delegated authentication");

        Check(handler.Requests.All(r => !r.Url.Contains("send", StringComparison.OrdinalIgnoreCase)), "no send endpoint used");

        var textHandler = new Handler((_, _) => Task.FromResult(Reply(HttpStatusCode.Created, "{\"id\":\"text\"}")));

        await Graph(textHandler).CreateDraftAsync(Message() with { Html = "" }, "text");

        Check(textHandler.Requests[0].Body.Contains("\"contentType\":\"Text\"") && textHandler.Requests[0].Body.Contains("Hello Alex"), "plain-text body supported");

        var connection = new Handler((_, _) => Task.FromResult(Reply(HttpStatusCode.OK, "{\"id\":\"drafts\"}")));

        await Graph(connection).ValidateConnectionAsync();

        Check(connection.Requests.Single().Method == "GET" && connection.Requests[0].Url.Contains("mailFolders/drafts"), "connection check is read only");

        var missing = new Handler((_, _) => throw new Exception("must not write"));

        try { await Graph(missing).CreateDraftAsync(Message(2, Path.Combine(root, "missing.pdf")), "missing"); throw new Exception("expected error"); }

        catch (FileNotFoundException) { Check(missing.Requests.Count == 0, "missing attachment prevents cloud writes"); }

        var large = Path.Combine(root, "large.pdf");

        using (var stream = File.Create(large)) stream.SetLength(4 * 1024 * 1024);

        var upload = new Handler((_, n) => Task.FromResult(n switch

        {

            0 => Reply(HttpStatusCode.Created, "{\"id\":\"large\"}"),

            1 => Reply(HttpStatusCode.OK, "{\"uploadUrl\":\"https://upload.example.test/session\"}"),

            2 => Reply(HttpStatusCode.OK, "{\"nextExpectedRanges\":[\"3276800-\"]}"),

            _ => Reply(HttpStatusCode.Created, "{\"id\":\"large-attachment\"}")

        }));

        await Graph(upload).CreateDraftAsync(Message(2, large), "large");

        Check(upload.Requests.Count == 4 && upload.Requests[1].Url.EndsWith("createUploadSession"), "large attachment uses upload session");

        Check(upload.Requests.Skip(2).All(r => r.Method == "PUT" && r.Auth == "") && upload.Requests[2].Range == "bytes 0-3276799/4194304" && upload.Requests[3].Range == "bytes 3276800-4194303/4194304", "upload chunks have exact ranges and no bearer token");

        var oversize = Path.Combine(root, "oversize.pdf");

        using (var stream = File.Create(oversize)) stream.SetLength(GraphDraftCreator.MaximumAttachmentBytes + 1);

        try { await Graph(missing).CreateDraftAsync(Message(2, oversize), "oversize"); throw new Exception("expected error"); }

        catch (InvalidDataException) { Check(missing.Requests.Count == 0, "oversized file blocked before cloud writes"); }

        File.Delete(oversize);

        foreach (var status in new[] { HttpStatusCode.Unauthorized, HttpStatusCode.TooManyRequests })

        {

            var rejected = new Handler((_, _) => Task.FromResult(Reply(status, "{\"error\":{\"message\":\"Denied\"}}")));

            try { await Graph(rejected).CreateDraftAsync(Message(), "rejected"); throw new Exception("expected error"); }

            catch (HttpRequestException ex) { Check(ex.StatusCode == status && rejected.Requests.Count == 1, $"{(int)status} rejection reported without retry"); }

        }

        var uncertain = new Handler((_, _) => Task.FromResult(Reply(HttpStatusCode.ServiceUnavailable, "{\"error\":{\"message\":\"Unavailable\"}}")));

        try { await Graph(uncertain).CreateDraftAsync(Message(), "uncertain"); throw new Exception("expected error"); }

        catch (DraftCreationException ex) { Check(ex.MayExist && uncertain.Requests.Count == 1, "server error requires review without retry"); }

        var partial = new Handler((_, n) => Task.FromResult(n == 0 ? Reply(HttpStatusCode.Created, "{\"id\":\"partial\"}") : Reply(HttpStatusCode.Forbidden, "{\"error\":{\"message\":\"Denied attachment\"}}")));

        try { await Graph(partial).CreateDraftAsync(Message(2, file), "partial"); throw new Exception("expected error"); }

        catch (DraftCreationException ex) { Check(ex.DraftId == "partial" && ex.MayExist, "attachment failure preserves draft ID for manual review"); }

        var cancelled = new Handler((_, _) => throw new OperationCanceledException());

        try { await Graph(cancelled).CreateDraftAsync(Message(), "cancelled"); throw new Exception("expected error"); }

        catch (DraftCreationException ex) { Check(ex.MayExist, "cancellation in flight requires review"); }

        using var cts = new CancellationTokenSource(); cts.Cancel();

        try { await Graph(missing).CreateDraftAsync(Message(), "cancelled", cts.Token); throw new Exception("expected error"); }

        catch (OperationCanceledException) { Check(missing.Requests.Count == 0, "cancellation before request has no cloud writes"); }

    }

    private static async Task BatchChecks()

    {

        var events = new List<string>();

        var creator = new Creator((_, id) => { events.Add("write " + id); return Task.FromResult(new DraftReceipt(id, "Drafts")); });

        var results = await new DraftBatchRunner().CreateAsync([Message(), Message(3)], creator, "batch",

            beforeAttempt: (_, id) => { events.Add("journal " + id); return Task.CompletedTask; },

            persist: r => { events.Add("save " + r.OperationId); return Task.CompletedTask; });

        Check(results.All(r => r.Status == "Draft created" && r.DraftId == r.OperationId) && events[0] == "journal batch-row-2" && events[1] == "write batch-row-2" && events[2] == "save batch-row-2", "journal precedes writes and results persist per row");

        var stopped = new Creator((_, _) => throw new DraftCreationException("incomplete", "partial"));

        results = await new DraftBatchRunner().CreateAsync([Message(), Message(3)], stopped, "stop");

        Check(stopped.Calls == 1 && results[0].Status == "Needs review" && results[1].Status == "Not attempted", "uncertain draft stops remaining batch");

        var failed = new Creator((m, _) => m.Row == 2 ? throw new InvalidDataException("known preflight failure") : Task.FromResult(new DraftReceipt("ok", "Drafts")));

        results = await new DraftBatchRunner().CreateAsync([Message(), Message(3)], failed, "continue");

        Check(failed.Calls == 2 && results[0].Status == "Failed" && results[1].Status == "Draft created", "definite failure logged with following row processed");

        var never = new Creator((_, _) => throw new Exception("unexpected write"));

        try { await new DraftBatchRunner().CreateAsync([Message()], never, "journal-failure", beforeAttempt: (_, _) => throw new IOException("disk full")); throw new Exception("expected error"); }

        catch (IOException) { Check(never.Calls == 0, "journal failure blocks external write"); }

        using var cts = new CancellationTokenSource(); cts.Cancel();

        results = await new DraftBatchRunner().CreateAsync([Message(), Message(3)], never, "cancel", cancellationToken: cts.Token);

        Check(never.Calls == 0 && results.All(r => r.Status == "Cancelled"), "batch cancellation skips remaining rows");

        var legacy = JsonSerializer.Deserialize<RunRecord>("{\"Id\":\"old\",\"Started\":\"2026-01-01T00:00:00Z\",\"OutputPath\":\"old\",\"Results\":[]}")!;

        Check(legacy.Mode == "Dry run" && legacy.Destination == "", "old history remains compatible");

    }

    private static void WorkflowChecks(string root)

    {

        var source = Path.Combine(root, "recipients.csv");

        File.WriteAllText(source, "Email,FirstName,AccountNo\na@example.com,Alex,00123\nb@example.com,Bob,00456");

        var dialogs = new Dialogs(source); var connector = new Connector(); var repo = new SqliteRunRepository(Path.Combine(root, "history.db"));

        var vm = new MainViewModel(new RecipientReader(), repo, dialogs, connector, draftLogRoot: Path.Combine(root, "runs"));

        Check(!vm.CreateDraftsCommand.CanExecute(null), "draft action disabled until connected and imported");

        Execute(vm.ImportCommand); Execute(vm.ConnectClassicCommand);

        Check(vm.ClassicAccounts.Count == 1 && vm.SelectedClassicAccount?.Email == "owner@example.com" && vm.CreateDraftsCommand.CanExecute(null), "classic connection selects explicit account");

        vm.To = "broken@"; Execute(vm.CreateDraftsCommand);

        Check(vm.Issues.Count > 0 && dialogs.Confirmations == 0 && connector.Creator.Calls == 0, "validation blocks confirmation and writes");

        vm.To = "{{Email}}"; dialogs.Confirm = false; Execute(vm.CreateDraftsCommand);

        Check(dialogs.Confirmations == 1 && connector.Creator.Calls == 0, "declined confirmation creates no drafts");

        dialogs.Confirm = true; Execute(vm.CreateDraftsCommand);

        Check(connector.Creator.Calls == 2 && vm.Results.All(r => r.Status == "Draft created") && dialogs.LastCount == 2, "confirmed workflow creates each merged draft once");

        Check(vm.History[0].Mode == "Outlook drafts" && File.Exists(Path.Combine(vm.OutputPath, "results.json")), "draft history and local journal stored");

        Execute(vm.HistoryCommand);

        Check(vm.History[0].Results.All(r => r.DraftId != null && r.OperationId != null), "SQLite reload preserves draft identifiers");

        vm.SelectedConnection = 1;

        Check(!vm.CreateDraftsCommand.CanExecute(null) && vm.ClassicAccounts.Count == 0, "changing provider disconnects old account");

        vm.MicrosoftClientId = Guid.NewGuid().ToString(); Execute(vm.ConnectMicrosoftCommand);

        Check(connector.MicrosoftConnections == 1 && vm.CreateDraftsCommand.CanExecute(null), "Microsoft connection enables drafts");

        vm.MicrosoftClientId = Guid.NewGuid().ToString();

        Check(!vm.CreateDraftsCommand.CanExecute(null), "editing Microsoft settings requires reconnect");

        var settingsFile = Path.Combine(root, "connection.json"); var store = new JsonConnectionSettingsStore(settingsFile);

        Wait(store.SaveAsync(new("fixture-client-id", "common")));

        var settings = Wait(store.LoadAsync());

        Check(settings.ClientId == "fixture-client-id" && !settings.EnableSending && JsonDocument.Parse(File.ReadAllText(settingsFile)).RootElement.EnumerateObject().Count() == 3, "connection file stores nonsecret client ID tenant and sending option");

        vm.CloseConnection();

        var failingConnector = new Connector();

        var failingVm = new MainViewModel(new RecipientReader(), new FailingRepository(), new Dialogs(source), failingConnector, draftLogRoot: Path.Combine(root, "failed-runs"));

        Execute(failingVm.ImportCommand); Execute(failingVm.ConnectClassicCommand); Execute(failingVm.CreateDraftsCommand);

        Check(failingConnector.Creator.Calls == 1 && !failingVm.IsBusy && failingVm.Results[0].Status == "Draft created", "result persistence failure preserves confirmed draft and releases busy state");

        Check(failingVm.Results[1].Status == "Not attempted" && failingVm.Results[1].Detail.Contains("Batch stopped") && !failingVm.Results.Any(r => r.Detail == "Waiting"), "logging failure stops remaining rows with actionable details");

        failingVm.CloseConnection();

    }

    private static async Task JournalChecks(string root)

    {

        var repo = new BlockingRepository();

        var snapshot = new RunRecord("timeout-fixture", DateTimeOffset.UtcNow, Path.Combine(root, "timeout-log"), []);

        try { await new DraftRunJournal(repo, TimeSpan.FromSeconds(2)).SaveAsync(snapshot); throw new Exception("expected timeout"); }

        catch (IOException ex) { Check(ex.Message.Contains("took too long") && repo.Started, "blocked result persistence times out instead of leaving batch waiting"); }

        Check(File.Exists(Path.Combine(snapshot.OutputPath, "results.json")) && repo.ContextWasNull, "journal file saved atomically and repository runs away from UI context");

        repo.Completion.SetResult();

    }

    private static void Execute(AsyncCommand command)

    {

        if (!command.CanExecute(null)) throw new Exception("Command unexpectedly disabled");

        command.Execute(null);

        var deadline = DateTime.UtcNow.AddSeconds(20);

        while (!command.CanExecute(null) && DateTime.UtcNow < deadline)

        { Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); Thread.Sleep(5); }

        if (DateTime.UtcNow >= deadline) throw new TimeoutException("Command did not finish");

    }

    private static void Wait(Task task) { while (!task.IsCompleted) { Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); Thread.Sleep(5); } task.GetAwaiter().GetResult(); }

    private static T Wait<T>(Task<T> task) { Wait((Task)task); return task.GetAwaiter().GetResult(); }

    private sealed record Request(string Method, string Url, string Body, string Auth, string Prefer, string Range);

    private sealed class Handler(Func<Request, int, Task<HttpResponseMessage>> action) : HttpMessageHandler

    {

        public List<Request> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)

        {

            var body = request.Content == null || request.Method == HttpMethod.Put ? "" : await request.Content.ReadAsStringAsync(token);

            var item = new Request(request.Method.Method, request.RequestUri!.AbsoluteUri, body, request.Headers.Authorization?.ToString() ?? "", request.Headers.TryGetValues("Prefer", out var values) ? string.Join(" ", values) : "", request.Content?.Headers.ContentRange?.ToString() ?? "");

            Requests.Add(item); return await action(item, Requests.Count - 1);

        }

    }

    private sealed class Creator(Func<MergedMessage, string, Task<DraftReceipt>> action) : IMailDraftCreator

    {

        public int Calls; public string Destination => "Fixture / owner@example.com / Drafts";

        public Task<DraftReceipt> CreateDraftAsync(MergedMessage message, string operationId, CancellationToken cancellationToken = default)

        { Calls++; return action(message, operationId); }

    }

    private sealed class Connector : IDraftConnector

    {

        public Creator Creator { get; } = new((_, id) => Task.FromResult(new DraftReceipt(id, "Fixture Drafts")));

        public int MicrosoftConnections;

        public Task<IReadOnlyList<DraftAccount>> ConnectClassicAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<DraftAccount>>([new("store", "Owner", "owner@example.com")]);

        public IMailDraftCreator UseClassicAccount(DraftAccount account) => Creator;

        public Task<IMailDraftCreator> ConnectMicrosoftAsync(MicrosoftConnectionSettings settings, CancellationToken token) { MicrosoftConnections++; return Task.FromResult<IMailDraftCreator>(Creator); }

        public void Disconnect() { }

        public void Dispose() { }

    }

    private sealed class Dialogs(string source) : IDesktopDialogs

    {

        public bool Confirm = true; public int Confirmations; public int LastCount;

        public bool ConfirmDraftCreation(int count, string destination) { Confirmations++; LastCount = count; return Confirm; }

        public string? OpenSource() => source;

        public string[] OpenAttachments() => [];

        public string? ChooseOutput() => null;

        public string? ProjectFile(bool save) => null;

    }

    private sealed class BlockingRepository : IRunRepository

    {

        public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool Started; public bool ContextWasNull;

        public Task SaveAsync(RunRecord run, CancellationToken token = default) { Started = true; ContextWasNull = SynchronizationContext.Current == null; return Completion.Task; }

        public Task<IReadOnlyList<RunRecord>> LoadAsync(CancellationToken token = default) => Task.FromResult<IReadOnlyList<RunRecord>>([]);

    }

    private sealed class FailingRepository : IRunRepository

    {

        private int writes;

        public Task SaveAsync(RunRecord run, CancellationToken token = default) => ++writes == 3 ? Task.FromException(new IOException("fixture persistence failure")) : Task.CompletedTask;

        public Task<IReadOnlyList<RunRecord>> LoadAsync(CancellationToken token = default) => Task.FromResult<IReadOnlyList<RunRecord>>([]);

    }

}
