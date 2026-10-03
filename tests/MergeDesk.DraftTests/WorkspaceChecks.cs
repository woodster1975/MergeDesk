using System.Data;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MergeDesk.App.Services;
using MergeDesk.App.ViewModels;
using MergeDesk.Core;
using MergeDesk.Infrastructure;

internal static class WorkspaceChecks
{
    private static int count;
    private static void Check(bool condition, string detail)
    { if (!condition) throw new Exception("FAIL: " + detail); count++; Console.WriteLine("PASS: " + detail); }
    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static void Wait(Task task)
    {
        var timeout = DateTime.UtcNow.AddSeconds(20);
        while (!task.IsCompleted && DateTime.UtcNow < timeout) { Pump(); Thread.Sleep(5); }
        if (!task.IsCompleted) throw new TimeoutException("Workspace check timed out");
        task.GetAwaiter().GetResult();
    }
    private static T Wait<T>(Task<T> task) { Wait((Task)task); return task.GetAwaiter().GetResult(); }
    private static void Execute(AsyncCommand command)
    {
        if (!command.CanExecute(null)) throw new Exception("Workspace command unexpectedly disabled");
        command.Execute(null); var timeout = DateTime.UtcNow.AddSeconds(20);
        while (!command.CanExecute(null) && DateTime.UtcNow < timeout) { Pump(); Thread.Sleep(5); }
        // Restore/dismiss can intentionally disable themselves on completion, so use Wait for those below.
        if (DateTime.UtcNow >= timeout) throw new TimeoutException("Workspace command did not complete");
    }
    private static void Until(Func<bool> predicate)
    { var timeout = DateTime.UtcNow.AddSeconds(20); while (!predicate() && DateTime.UtcNow < timeout) { Pump(); Thread.Sleep(5); } if (!predicate()) throw new TimeoutException("Workspace state did not settle"); }
    private static void Exclude(MainViewModel model, int row)
    { var record = model.RecipientView!.Table!.Rows.Cast<DataRow>().Single(r => (int)r[model.SourceRowColumn] == row); record[model.InclusionColumn] = false; }
    public static int Run(string root)
    {
        count = 0;
        var path = Path.Combine(root, "workspace.csv");
        File.WriteAllText(path, "Email,FirstName,Reference\nalex@example.com,Alex,A1\njordan@example.com,Jordan,J2\ninvalid-email,Sam,S3\n");
        var dialogs = new Dialogs(path, Path.Combine(root, "selected-exports"));
        var connector = new Connector();
        var ledgerPath = Path.Combine(root, "selected-send.db");
        var model = new MainViewModel(new RecipientReader(), new SqliteRunRepository(Path.Combine(root, "workspace-history.db")), dialogs, connector,
            draftLogRoot: Path.Combine(root, "workspace-runs"), sendLedger: new SqliteSendLedger(ledgerPath),
            testLedger: new SqliteSendLedger(Path.Combine(root, "workspace-tests.db")));
        Execute(model.ImportCommand); Exclude(model, 4);
        model.Html = "<p>Hello {{FirstName}}</p>"; model.Text = "Hello {{FirstName}}"; model.Subject = "Update for {{FirstName}}";
        Execute(model.ValidateCommand);
        Check(model.Issues.Count == 0 && model.IncludedCount == 2, "excluded invalid recipient does not block validation of included rows");
        model.RecipientSearch = "Alex";
        Check(model.RecipientView!.Count == 1 && model.IncludedCount == 2, "search changes visible rows without changing batch membership");
        model.RecipientSearch = "'[%;_";
        Check(model.RecipientView.Count == 0 && model.IncludedCount == 2, "search treats punctuation as literal text without expression injection");
        model.RecipientSearch = "Alex"; Execute(model.ExcludeShownCommand);
        Check(model.IncludedCount == 1 && !model.ProjectSnapshot().Selection!.ExcludedRows.Contains(3), "exclude shown leaves hidden included recipients unchanged");
        Execute(model.IncludeShownCommand); model.RecipientSearch = "";
        Execute(model.RunCommand);
        Check(model.Results.Count == 2 && model.Results.All(r => r.Row is 2 or 3 && r.Status == "Exported"), "dry run exports only ticked original source rows");
        Execute(model.ConnectClassicCommand); Execute(model.CreateDraftsCommand);
        Check(dialogs.LastDraftCount == 2 && connector.Provider.Created.TakeLast(2).Select(m => m.Row).SequenceEqual([2,3]), "draft count and payloads contain only included rows");
        connector.Provider.Created.Clear(); connector.Provider.Submitted = 0;
        model.SelectedRecipient = model.Recipients.Single(r => r.Row == 4); model.TestAddress = "reviewer@example.com";
        model.Html += "<img src=\"data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=\"/>";
        model.Cc = "copy@example.com"; model.Bcc = "hidden@example.com";
        var file = Path.Combine(root, "S3.pdf"); File.WriteAllText(file, "test attachment"); model.Patterns = Path.Combine(root, "{{Reference}}.pdf");
        model.TestAddress = "reviewer@example.com;other@example.com"; Execute(model.SendTestCommand);
        Check(dialogs.TestConfirmations == 0 && connector.Provider.Submitted == 0, "test rejects multiple addresses before confirmation or mailbox writes");
        model.TestAddress = "reviewer@example.com"; dialogs.ConfirmTest = false; Execute(model.SendTestCommand);
        Check(connector.Provider.Created.Count == 0 && dialogs.LastTest?.Row == 4, "declined test confirmation makes no mailbox changes");
        dialogs.ConfirmTest = true; model.SendIntervalSeconds = 0; Execute(model.SendTestCommand);
        var sentTest = connector.Provider.Created.Single();
        Check(sentTest.To == "reviewer@example.com" && sentTest.Cc == "" && sentTest.Bcc == "" && sentTest.Html.Contains("Sam") && sentTest.Attachments.Single() == file,
            "test uses one selected row's content and files and clears original To CC BCC");
        Check(sentTest.Subject == "[TEST] Update for Sam" && model.History[0].Mode == "Test email" && model.Results.Count == 1, "test subject and history are clearly labelled");
        Check(sentTest.InlineImages?.Count == 1 && sentTest.Html.Contains("cid:"), "test email retains the selected template's embedded image resources");
        Execute(model.SendTestCommand);
        Check(connector.Provider.Submitted == 1 && model.Results.Single().Status == "Already submitted", "identical confirmed test is skipped by its separate durable ledger");
        var reservation = Wait(new SqliteSendLedger(ledgerPath).ReserveAsync(Wait(LiveSendRunner.FingerprintAsync(sentTest, connector.Provider.Destination)), "production-fixture"));
        Check(reservation.Owned, "test submission cannot claim or skip a matching production message");
        model.Patterns = ""; model.SendIntervalSeconds = 1; model.Cc = ""; model.Bcc = "";
        Execute(model.SendCommand);
        Check(dialogs.LastSendCount == 2 && model.Results.Select(r => r.Row).SequenceEqual([2,3]), "live confirmation and submission use included recipients only despite excluded preview row");
        model.Mappings.Single(m => m.Field == "Reference").Field = "LetterReference";
        Execute(model.ReloadCommand);
        Check(model.IncludedCount == 2 && model.Mappings.Any(m => m.Field == "LetterReference"), "unchanged reload preserves exclusions and mapping aliases");
        File.AppendAllText(path, "new@example.com,New,N4\n"); Execute(model.ReloadCommand);
        Check(model.IncludedCount == 0 && model.Recipients.Count == 4 && model.Status.Contains("source data changed", StringComparison.OrdinalIgnoreCase), "changed source file unticks every row until the user reviews it");
        Execute(model.ValidateCommand);
        Check(model.Issues.Single().Area == "Recipients" && !model.SendCommand.CanExecute(null), "empty selection blocks sending with an actionable validation issue");
        model.CloseConnection();
        RecoveryChecks(root, path);
        UiCheck(root, path);
        return count;
    }
    private static void RecoveryChecks(string root, string source)
    {
        var recoveryFolder = Path.Combine(root, "recovery-fixture");
        var recovery = new JsonProjectRecoveryStore(recoveryFolder);
        var model = new MainViewModel(new RecipientReader(), new SqliteRunRepository(Path.Combine(root, "recover-history.db")), new Dialogs(source, root),
            draftLogRoot: Path.Combine(root, "recover-runs"), recoveryStore: recovery);
        Wait(model.LoadRecoveryAsync()); Execute(model.ImportCommand); Exclude(model, 3);
        model.Html = "<p>Recovered {{FirstName}}</p>"; model.Subject = "Recovered subject"; model.TestAddress = "me@example.com"; model.SendIntervalSeconds = 7;
        model.Mappings[0].Field = "Mail"; model.To = "{{Mail}}";
        Check(Wait(model.FlushAutosaveAsync()) && Directory.GetFiles(recoveryFolder, "*.json").Length == 1 && Directory.GetFiles(recoveryFolder, "*.tmp").Length == 0,
            "autosave writes one complete snapshot atomically and leaves no temporary file");
        using (var parallel = new JsonProjectRecoveryStore(recoveryFolder))
            Check(Wait(parallel.LoadAsync()) == null, "another open app instance cannot recover an active workspace");
        model.CloseConnection();
        using var next = new JsonProjectRecoveryStore(recoveryFolder);
        var recoveredConnector = new Connector();
        var restored = new MainViewModel(new RecipientReader(), new SqliteRunRepository(Path.Combine(root,"restore-history.db")), new Dialogs(source,root), recoveredConnector,
            draftLogRoot: Path.Combine(root,"restore-runs"), recoveryStore: next);
        Wait(restored.LoadRecoveryAsync());
        Check(restored.HasPendingRecovery && !restored.IsEditable && !restored.ImportCommand.CanExecute(null), "recovery is offered explicitly before new workspace edits");
        restored.RestoreRecoveryCommand.Execute(null); Until(() => !restored.HasPendingRecovery);
        Check(restored.Html.Contains("Recovered") && restored.Subject == "Recovered subject" && restored.TestAddress == "me@example.com" && restored.SendIntervalSeconds == 7,
            "recovery restores message settings and test address");
        Check(restored.IncludedCount == 3 && restored.ProjectSnapshot().Selection!.ExcludedRows.SequenceEqual([3]) && restored.Mappings[0].Field == "Mail",
            "recovery restores exact recipient ticks and mapping aliases when source is unchanged");
        Check(recoveredConnector.Connects == 0 && !restored.CanSend, "restoring a workspace never reconnects Outlook or resumes sending");
        restored.Subject = "Last edit before close"; Check(Wait(restored.FlushAutosaveAsync()), "explicit close flush persists edits before debounce fires");
        restored.CloseConnection();
        using (var reader = new JsonProjectRecoveryStore(recoveryFolder))
        { var state=Wait(reader.LoadAsync()); Check(state?.Project.Template.Subject == "Last edit before close", "latest flushed recovery snapshot survives a new store instance"); Wait(reader.AcknowledgeAsync()); }
        // Legacy project JSON omits newly added properties and remains readable.
        var legacy = JsonSerializer.Deserialize<MergeProject>("{\"SourcePath\":\"\",\"Worksheet\":1,\"Mappings\":[],\"Template\":{\"To\":\"\",\"Cc\":\"\",\"Bcc\":\"\",\"Subject\":\"Old\",\"Html\":\"<p>Hello</p>\",\"Text\":\"Hello\",\"GlobalAttachments\":[],\"AttachmentPatterns\":[]}}");
        Check(legacy?.Selection == null && legacy?.SendIntervalSeconds == 3, "older saved projects load with compatible selection and interval defaults");
        using (var seed = new JsonProjectRecoveryStore(recoveryFolder)) { Wait(seed.SaveAsync(model.ProjectSnapshot())); }
        File.AppendAllText(source,"later@example.com,Later,L5\n");
        using (var changedStore = new JsonProjectRecoveryStore(recoveryFolder))
        {
            var changed = new MainViewModel(new RecipientReader(), new SqliteRunRepository(Path.Combine(root,"changed-history.db")), new Dialogs(source,root), recoveryStore:changedStore, draftLogRoot:Path.Combine(root,"changed-runs"));
            Wait(changed.LoadRecoveryAsync()); changed.RestoreRecoveryCommand.Execute(null); Until(() => !changed.HasPendingRecovery);
            Check(changed.IncludedCount == 0 && changed.Recipients.Count == 5, "recovery against changed recipient data requires fresh inclusion choices"); changed.CloseConnection();
        }
        var missingFolder = Path.Combine(root,"missing-recovery");
        var project=model.ProjectSnapshot() with { SourcePath=Path.Combine(root,"missing.csv") };
        using (var seed=new JsonProjectRecoveryStore(missingFolder)) Wait(seed.SaveAsync(project));
        using (var missingStore = new JsonProjectRecoveryStore(missingFolder))
        {
            var missing = new MainViewModel(new RecipientReader(), new SqliteRunRepository(Path.Combine(root,"missing-history.db")),new Dialogs(source,root), recoveryStore:missingStore,draftLogRoot:Path.Combine(root,"missing-runs"));
            Wait(missing.LoadRecoveryAsync()); missing.RestoreRecoveryCommand.Execute(null); Until(()=>!missing.HasPendingRecovery);
            Check(missing.Recipients.Count==0 && missing.Subject==project.Template.Subject && missing.Mappings.Count==project.Mappings.Length && missing.ProjectSnapshot().Selection?.ExcludedRows.SequenceEqual(project.Selection!.ExcludedRows)==true,
                "missing source recovery preserves template mappings and selection for later reload"); missing.CloseConnection();
        }
        var failed = new MainViewModel(new RecipientReader(),new SqliteRunRepository(Path.Combine(root,"failed-recovery-history.db")),new Dialogs(source,root),recoveryStore:new FailingRecovery(),draftLogRoot:Path.Combine(root,"failed-recovery-runs"));
        Wait(failed.LoadRecoveryAsync());failed.Subject="Unsaved edit";
        Check(!Wait(failed.FlushAutosaveAsync()) && failed.AutosaveStatus.Contains("save your project"), "autosave failures remain visible and report that manual save is needed"); failed.CloseConnection();
        using var dismissStore=new JsonProjectRecoveryStore(recoveryFolder);
        var dismissed=new MainViewModel(new RecipientReader(),new SqliteRunRepository(Path.Combine(root,"dismiss-history.db")),new Dialogs(source,root),recoveryStore:dismissStore,draftLogRoot:Path.Combine(root,"dismiss-runs"));
        Wait(dismissed.LoadRecoveryAsync());dismissed.DismissRecoveryCommand.Execute(null);Until(()=>!dismissed.HasPendingRecovery);
        Check(dismissed.IsEditable && dismissed.Recipients.Count==0 && dismissed.Html.Contains("[Write your message here"), "start fresh dismisses recovery without silently applying it"); dismissed.CloseConnection();
    }
    private static void UiCheck(string root, string source)
    {
        var window = new MergeDesk.App.MainWindow();
        var model = new MainViewModel(new RecipientReader(),new SqliteRunRepository(Path.Combine(root,"ui-select-history.db")),new Dialogs(source,root),draftLogRoot:Path.Combine(root,"ui-select-runs"));
        window.DataContext=model;Execute(model.ImportCommand);
        var visual=(FrameworkElement)window.Content;
        visual.Measure(new Size(1200,820));visual.Arrange(new Rect(0,0,1200,820));visual.UpdateLayout();Pump();
        var grid=(DataGrid)window.FindName("RecipientGrid");
        Check(grid.Columns[0] is DataGridTemplateColumn && Equals(grid.Columns[0].Header,"Include") && grid.Columns.All(c=>c.Header?.ToString()!=model.MatchColumn), "recipient grid exposes single-click include ticks and hides search implementation data");
        var row=(DataGridRow)grid.ItemContainerGenerator.ContainerFromIndex(0);
        var presenter=(ContentPresenter)grid.Columns[0].GetCellContent(row);
        presenter.ApplyTemplate(); var checkbox=(CheckBox)System.Windows.Media.VisualTreeHelper.GetChild(presenter,0);
        checkbox.IsChecked=false;
        Check(model.IncludedCount==model.Recipients.Count-1 && model.ProjectSnapshot().Selection!.ExcludedRows.Contains(2), "real WPF checkbox changes the included batch through its binding");
        window.Close();Pump();
    }
    private sealed class Dialogs(string source,string output):IDesktopDialogs
    {
        public bool ConfirmTest=true;public int TestConfirmations,LastDraftCount,LastSendCount;public MergedMessage? LastTest;
        public bool ConfirmTestSending(MergedMessage message,string destination){TestConfirmations++;LastTest=message;return ConfirmTest;}
        public bool ConfirmSending(int count,string destination,int intervalSeconds){LastSendCount=count;return true;}
        public bool ConfirmDraftCreation(int count,string destination){LastDraftCount=count;return true;}
        public string? OpenSource()=>source;public string[] OpenAttachments()=>[];public string? ChooseOutput()=>output;public string? ProjectFile(bool save)=>null;
    }
    private sealed class Connector:IDraftConnector
    {
        public Sender Provider {get;}=new();public int Connects;
        public Task<IReadOnlyList<DraftAccount>> ConnectClassicAsync(CancellationToken token){Connects++;return Task.FromResult<IReadOnlyList<DraftAccount>>([new("store","Owner","owner@example.com")]);}
        public IMailDraftCreator UseClassicAccount(DraftAccount account)=>Provider;
        public Task<IMailDraftCreator> ConnectMicrosoftAsync(MicrosoftConnectionSettings settings,CancellationToken token){Connects++;return Task.FromResult<IMailDraftCreator>(Provider);}
        public void Disconnect(){}public void Dispose(){}
    }
    private sealed class Sender:IMailDelivery
    {
        public List<MergedMessage> Created {get;}=[];public int Submitted;
        public string Destination=>"Classic Outlook / owner@example.com / Drafts";public string SubmissionStatus=>"Submitted to Outlook";
        public Task<DraftReceipt> CreateDraftAsync(MergedMessage message,string operationId,CancellationToken cancellationToken=default){Created.Add(message);return Task.FromResult(new DraftReceipt(operationId,Destination));}
        public Task SubmitDraftAsync(string id,CancellationToken token=default){Submitted++;return Task.CompletedTask;}
        public Task<string> SendAsync(MergedMessage message,CancellationToken cancellationToken=default)=>throw new NotSupportedException();
    }
    private sealed class FailingRecovery:IProjectRecoveryStore
    { public Task<RecoverySnapshot?> LoadAsync()=>Task.FromResult<RecoverySnapshot?>(null);public Task SaveAsync(MergeProject project)=>Task.FromException(new IOException("fixture write denied"));public Task AcknowledgeAsync()=>Task.CompletedTask; }
}
