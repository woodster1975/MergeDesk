using System.Collections.ObjectModel;

using System.Data;

using System.IO;

using System.Text.Json;

using MergeDesk.App.Services;

using MergeDesk.Core;

using MergeDesk.Infrastructure;



namespace MergeDesk.App.ViewModels;



public sealed partial class MainViewModel : ObservableObject

{

    private readonly IRecipientReader reader;

    private readonly IRunRepository repository;

    private readonly IDesktopDialogs dialogs;

    private readonly IDraftConnector? draftConnector;

    private readonly IConnectionSettingsStore? connectionSettings;

    private readonly string draftLogRoot;

    private readonly ISendLedger sendLedger;

    private bool enableMicrosoftSending;

    private int sendIntervalSeconds = 3;

    private IMailDraftCreator? draftCreator;

    private int selectedConnection;

    private DraftAccount? selectedClassicAccount;

    private string microsoftClientId = "", microsoftTenant = "common", connectionSummary = "Choose a connection and sign in before creating drafts.";

    private readonly MergeEngine engine = new();

    private RecipientData data = new([], []);

    private readonly List<AsyncCommand> commands = [];

    private CancellationTokenSource? cancellation;

    private string sourcePath = "", status = "Load a recipient file to start. Preview before drafting or sending.", outputPath = "";

    private string to = "{{Email}}", cc = "", bcc = "", subject = "An update for you";

    private string html = "<p>Hello,</p>\n<p>[Write your message here. Use Insert merge field to add details from your recipient data.]</p>\n<p>Kind regards,<br>[Your name or team]</p>";

    private string text = "Hello,\n\n[Write your message here. Use Insert merge field to add details from your recipient data.]\n\nKind regards,\n[Your name or team]";

    private string patterns = "", previewText = "Choose a recipient and refresh the preview.", progress = "", previewHtml = "";

    private int worksheet = 1, selectedTab;

    private bool busy;

    private Recipient? selectedRecipient;

    private DataView? recipientView;

    public MainViewModel(IRecipientReader reader, IRunRepository repository, IDesktopDialogs dialogs,

        IDraftConnector? draftConnector = null, IConnectionSettingsStore? connectionSettings = null, string? draftLogRoot = null, ISendLedger? sendLedger = null, IProjectRecoveryStore? recoveryStore = null, ISendLedger? testLedger = null, IMessageLibrary? messageLibrary = null)

    {

        this.reader = reader; this.repository = repository; this.dialogs = dialogs;

        this.draftConnector = draftConnector; this.connectionSettings = connectionSettings;

        this.draftLogRoot = draftLogRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MergeDesk", "runs");

        this.sendLedger = sendLedger ?? new SqliteSendLedger(Path.Combine(Path.GetDirectoryName(this.draftLogRoot)!, "send-ledger.db"));

        this.recoveryStore = recoveryStore;

        this.testLedger = testLedger ?? new SqliteSendLedger(Path.Combine(Path.GetDirectoryName(this.draftLogRoot)!, "test-send-ledger.db"));

        if (recoveryStore != null)

        {

            autosaveTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };

            autosaveTimer.Tick += async (_, _) => await FlushAutosaveAsync();

        }

        AsyncCommand Command(Func<Task> action, Func<bool>? enabled = null)

        {

            var command = new AsyncCommand(async () => { try { await action(); } catch (Exception ex) { Status = $"Action failed: {ex.Message}"; } }, () => !IsBusy && !HasPendingRecovery && (enabled?.Invoke() ?? true));

            commands.Add(command); return command;

        }

        library = messageLibrary;
        SaveTemplateCommand = Command(SaveTemplateAsync, () => library != null && !string.IsNullOrWhiteSpace(TemplateName));
        UseTemplateCommand = Command(UseTemplateAsync, () => SelectedTemplate != null);
        DeleteTemplateCommand = Command(() => DeleteLibraryAsync(SelectedTemplate), () => SelectedTemplate != null);
        NewTemplateCommand = Command(() => { SelectedTemplate = null; TemplateName = ""; return Task.CompletedTask; });
        NewSignatureCommand = Command(() => EditSignatureAsync(null), () => library != null);
        EditSignatureCommand = Command(() => EditSignatureAsync(SelectedSignature), () => SelectedSignature != null);
        AppendSignatureCommand = Command(AppendSignatureAsync, () => SelectedSignature != null);
        DeleteSignatureCommand = Command(() => DeleteLibraryAsync(SelectedSignature), () => SelectedSignature != null);
        RefreshLibraryCommand = Command(LoadLibraryAsync, () => library != null);
        ImportCommand = Command(async () => { if (dialogs.OpenSource() is { } file) await ImportAsync(file); });

        ReloadCommand = Command(async () => await ImportAsync(SourcePath, FieldMappings(), Selection()), () => File.Exists(SourcePath));

        AddMappingCommand = Command(() => { Mappings.Add(new("NewField", Columns.FirstOrDefault() ?? "")); return Task.CompletedTask; }, () => Columns.Count > 0);

        AddAttachmentCommand = Command(() => { foreach (var file in dialogs.OpenAttachments()) if (!Attachments.Contains(file)) Attachments.Add(file); return Task.CompletedTask; });

        ClearAttachmentsCommand = Command(() => { Attachments.Clear(); return Task.CompletedTask; });

        ValidateCommand = Command(() => { Validate(); return Task.CompletedTask; }, () => data.Recipients.Count > 0);

        PreviewCommand = Command(() => { UpdatePreview(); return Task.CompletedTask; }, () => SelectedRecipient != null);

        RunCommand = Command(RunAsync, () => IncludedCount > 0);

        ConnectClassicCommand = Command(ConnectClassicAsync, () => IsClassicConnection && draftConnector != null);

        ConnectMicrosoftCommand = Command(ConnectMicrosoftAsync, () => IsMicrosoftConnection && draftConnector != null);

        DisconnectCommand = Command(() => { ResetConnection(); return Task.CompletedTask; }, () => draftCreator != null || ClassicAccounts.Count > 0);

        CreateDraftsCommand = Command(CreateDraftsAsync, () => IncludedCount > 0 && draftCreator != null);

        SendCommand = Command(SendLiveAsync, () => IncludedCount > 0 && draftCreator is IMailDelivery);

        IncludeShownCommand = Command(() => { SetInclusion(true); return Task.CompletedTask; }, () => RecipientView?.Count > 0);

        ExcludeShownCommand = Command(() => { SetInclusion(false); return Task.CompletedTask; }, () => RecipientView?.Count > 0);

        ExcludeAllCommand = Command(() => { SetInclusion(false, all: true); return Task.CompletedTask; }, () => Recipients.Count > 0);

        SendTestCommand = Command(SendTestAsync, () => SelectedRecipient != null && draftCreator is IMailDelivery);

        RestoreRecoveryCommand = new AsyncCommand(async () => { try { await RestoreRecoveryAsync(); } catch (Exception ex) { AutosaveStatus = "Recovery failed: " + ex.Message; } }, () => HasPendingRecovery && !IsBusy);

        DismissRecoveryCommand = new AsyncCommand(async () => { try { await DismissRecoveryAsync(); } catch (Exception ex) { AutosaveStatus = "Could not dismiss recovery: " + ex.Message; } }, () => HasPendingRecovery && !IsBusy);

        SaveProjectCommand = Command(SaveProjectAsync);

        OpenProjectCommand = Command(OpenProjectAsync);

        HistoryCommand = Command(LoadHistoryAsync);
        ClearHistoryCommand = Command(ClearHistoryAsync);


        CancelCommand = new AsyncCommand(() => { cancellation?.Cancel(); return Task.CompletedTask; }, () => IsBusy);

        Mappings.CollectionChanged += (_, e) =>

        {

            if (e.NewItems != null) foreach (MappingRow item in e.NewItems) item.PropertyChanged += (_, _) => Invalidate();

            Invalidate();

        };

        Attachments.CollectionChanged += (_, _) => Invalidate();

    }

    public AsyncCommand ImportCommand { get; }

    public AsyncCommand ReloadCommand { get; }

    public AsyncCommand AddMappingCommand { get; }

    public AsyncCommand AddAttachmentCommand { get; }

    public AsyncCommand ClearAttachmentsCommand { get; }

    public AsyncCommand ValidateCommand { get; }

    public AsyncCommand PreviewCommand { get; }

    public AsyncCommand RunCommand { get; }

    public AsyncCommand ConnectClassicCommand { get; }

    public AsyncCommand ConnectMicrosoftCommand { get; }

    public AsyncCommand DisconnectCommand { get; }

    public AsyncCommand CreateDraftsCommand { get; }

    public AsyncCommand SendCommand { get; }

    public bool CanSend => draftCreator is IMailDelivery;

    public bool EnableMicrosoftSending { get => enableMicrosoftSending; set { if (Set(ref enableMicrosoftSending, value) && IsMicrosoftConnection) ResetConnection(); } }

    public int SendIntervalSeconds { get => sendIntervalSeconds; set { if (Set(ref sendIntervalSeconds, value)) WorkspaceChanged(); } }

    public AsyncCommand SaveProjectCommand { get; }

    public AsyncCommand OpenProjectCommand { get; }

    public AsyncCommand HistoryCommand { get; }
    public AsyncCommand ClearHistoryCommand { get; }

    public AsyncCommand CancelCommand { get; }

    public ObservableCollection<string> Columns { get; } = [];

    public ObservableCollection<MappingRow> Mappings { get; } = [];

    public ObservableCollection<string> Attachments { get; } = [];

    public ObservableCollection<Recipient> Recipients { get; } = [];

    public ObservableCollection<ValidationIssue> Issues { get; } = [];

    public ObservableCollection<RunResult> Results { get; } = [];

    public ObservableCollection<RunRecord> History { get; } = [];

    public ObservableCollection<DraftAccount> ClassicAccounts { get; } = [];

    public string[] ConnectionOptions { get; } = ["Classic Outlook on this computer", "Microsoft 365 / Outlook.com (new or classic Outlook)"];

    public int SelectedConnection

    {

        get => selectedConnection;

        set { if (!IsBusy && Set(ref selectedConnection, value)) { ResetConnection(); Changed(nameof(IsClassicConnection)); Changed(nameof(IsMicrosoftConnection)); foreach (var command in commands) command.Refresh(); } }

    }

    public bool IsClassicConnection => SelectedConnection == 0;

    public bool IsMicrosoftConnection => SelectedConnection == 1;

    public string MicrosoftClientId { get => microsoftClientId; set { if (Set(ref microsoftClientId, value) && IsMicrosoftConnection) ResetConnection(); } }

    public string MicrosoftTenant { get => microsoftTenant; set { if (Set(ref microsoftTenant, value) && IsMicrosoftConnection) ResetConnection(); } }

    public string ConnectionSummary { get => connectionSummary; private set => Set(ref connectionSummary, value); }

    public DraftAccount? SelectedClassicAccount

    {

        get => selectedClassicAccount;

        set

        {

            if (!Set(ref selectedClassicAccount, value)) return;

            draftCreator = value == null ? null : draftConnector?.UseClassicAccount(value);

            ConnectionSummary = draftCreator?.Destination ?? "Select an Outlook account.";

            CreateDraftsCommand.Refresh(); DisconnectCommand.Refresh(); SendCommand.Refresh(); SendTestCommand.Refresh(); Changed(nameof(CanSend));

        }

    }

    private void ResetConnection()

    {

        draftCreator = null; selectedClassicAccount = null; Changed(nameof(SelectedClassicAccount)); ClassicAccounts.Clear();

        draftConnector?.Disconnect(); ConnectionSummary = "Not connected. Choose a connection and sign in.";

        CreateDraftsCommand.Refresh(); DisconnectCommand.Refresh(); SendCommand.Refresh(); SendTestCommand.Refresh(); Changed(nameof(CanSend));

    }

    public void CloseConnection() { autosaveTimer?.Stop(); (recoveryStore as IDisposable)?.Dispose(); draftConnector?.Dispose(); }

    public async Task LoadConnectionSettingsAsync()

    {

        if (connectionSettings == null) return;

        var settings = await connectionSettings.LoadAsync(); MicrosoftClientId = settings.ClientId; MicrosoftTenant = settings.Tenant; EnableMicrosoftSending = settings.EnableSending;

    }

    private async Task ConnectClassicAsync()

    {

        ResetConnection(); IsBusy = true; cancellation = new(); Status = "Connecting to classic Outlook…";

        try

        {

            var accounts = await draftConnector!.ConnectClassicAsync(cancellation.Token);

            foreach (var account in accounts) ClassicAccounts.Add(account);

            SelectedClassicAccount = ClassicAccounts.FirstOrDefault();

            Status = accounts.Count == 0 ? "No usable Outlook accounts found. Configure an account in classic Outlook, then reconnect." : "Connected. Select the account whose Drafts folder should receive your messages.";

        }

        finally { cancellation.Dispose(); cancellation = null; IsBusy = false; DisconnectCommand.Refresh(); }

    }

    private async Task ConnectMicrosoftAsync()

    {

        ResetConnection(); var settings = new MicrosoftConnectionSettings(MicrosoftClientId.Trim(), MicrosoftTenant.Trim(), EnableMicrosoftSending);

        IsBusy = true; cancellation = new(); Status = "Complete Microsoft sign-in in your browser. You can cancel here.";

        try

        {

            draftCreator = await draftConnector!.ConnectMicrosoftAsync(settings, cancellation.Token);

            ConnectionSummary = draftCreator.Destination; Status = "Microsoft mailbox connected. Drafts will appear in Outlook's Drafts folder.";

            Changed(nameof(CanSend));

            if (connectionSettings != null)

            {

                try { await connectionSettings.SaveAsync(settings); }

                catch (Exception ex) { Status = $"Connected, but setup settings could not be saved: {ex.Message}"; }

            }

        }

        finally { cancellation.Dispose(); cancellation = null; IsBusy = false; }

    }

    public string SourcePath { get => sourcePath; private set => Set(ref sourcePath, value); }

    public int Worksheet { get => worksheet; set { if (Set(ref worksheet, value)) { Status = "Worksheet changed. Reload the file to apply it."; WorkspaceChanged(); } } }

    public string Status { get => status; private set => Set(ref status, value); }

    public string OutputPath { get => outputPath; private set => Set(ref outputPath, value); }

    public string Progress { get => progress; private set => Set(ref progress, value); }

    public int SelectedTab { get => selectedTab; set => Set(ref selectedTab, value); }

    public bool IsBusy { get => busy; private set { if (Set(ref busy, value)) { Changed(nameof(IsEditable)); foreach (var command in commands) command.Refresh(); CancelCommand.Refresh(); if (!busy && recoveryReady && !applyingProject && !HasPendingRecovery && revision != savedRevision) autosaveTimer?.Start(); } } }

    public bool IsEditable => !IsBusy && !HasPendingRecovery;

    public string To { get => to; set { if (Set(ref to, value)) Invalidate(); } }

    public string Cc { get => cc; set { if (Set(ref cc, value)) Invalidate(); } }

    public string Bcc { get => bcc; set { if (Set(ref bcc, value)) Invalidate(); } }

    public string Subject { get => subject; set { if (Set(ref subject, value)) Invalidate(); } }

    public string Html { get => html; set { if (Set(ref html, value)) Invalidate(); } }

    public string Text { get => text; set { if (Set(ref text, value)) Invalidate(); } }

    public string Patterns { get => patterns; set { if (Set(ref patterns, value)) Invalidate(); } }

    public string PreviewText { get => previewText; private set => Set(ref previewText, value); }

    public string PreviewHtml { get => previewHtml; private set => Set(ref previewHtml, value); }

    public Recipient? SelectedRecipient { get => selectedRecipient; set { if (Set(ref selectedRecipient, value)) { PreviewCommand.Refresh(); SendTestCommand.Refresh(); UpdatePreview(); } } }

    public DataView? RecipientView { get => recipientView; private set => Set(ref recipientView, value); }

    public string RecipientSummary => Recipients.Count == 0 ? "No data loaded" : $"{Recipients.Count:N0} recipients · {Columns.Count} columns";

    private void Invalidate() { Issues.Clear(); PreviewHtml = ""; PreviewText = "Template or mapping changed. Refresh the preview to see the latest message."; WorkspaceChanged(); }

    private MergeTemplate Template() => new(To, Cc, Bcc, Subject, Html, Text, Attachments.ToArray(), Patterns.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private FieldMapping[] FieldMappings() => Mappings.Select(m => new FieldMapping(m.Field, m.Column)).ToArray();



    private async Task ImportAsync(string path, FieldMapping[]? savedMappings = null, RecipientSelection? selection = null)

    {

        if (savedMappings == null && pendingTemplateMappings) savedMappings = FieldMappings();
        IsBusy = true; Status = "Reading recipients…";

        try

        {

            var sheet = Worksheet;

            var imported = await Task.Run(() => reader.Read(path, sheet));

            data = imported; SourcePath = path; pendingTemplateMappings = false;

            Columns.Clear(); foreach (var column in data.Columns) Columns.Add(column);

            Mappings.Clear(); foreach (var mapping in savedMappings ?? data.Columns.Select(c => new FieldMapping(c, c)).ToArray()) Mappings.Add(new(mapping.Field, mapping.Column));

            Recipients.Clear(); foreach (var recipient in data.Recipients) Recipients.Add(recipient);

            var changedSource = BuildRecipientTable(selection); SelectedRecipient = Recipients.FirstOrDefault();

            Changed(nameof(RecipientSummary));

            Status = changedSource ? "The source data changed. All recipients are excluded; review the list and tick the rows to include." : $"Loaded {RecipientSummary}. {InclusionSummary}. Review mappings and compose your message.";



        }

        finally { IsBusy = false; }

    }

    private MergeBatch Validate()

    {

        var batch = engine.Merge(IncludedData(), FieldMappings(), Template());

        Issues.Clear(); foreach (var issue in batch.Issues) Issues.Add(issue);

        if (Recipients.Count > 0 && IncludedCount == 0) { Issues.Clear(); Issues.Add(new(0, "Recipients", "Select at least one recipient using the Include ticks in Recipients.")); batch = new(batch.Messages, Issues.ToArray()); }

        Status = batch.Issues.Count == 0 ? $"Validation passed for {batch.Messages.Count} messages. Ready for a dry run or draft creation." : $"{batch.Issues.Count} validation issues. Resolve all issues before running.";

        return batch;

    }

    private void UpdatePreview()

    {

        if (SelectedRecipient == null) return;

        var batch = engine.Merge(new(data.Columns, [SelectedRecipient]), FieldMappings(), Template());

        var message = batch.Messages.Single();

        PreviewHtml = InlineImageTools.PreviewHtml(message);

        PreviewText = $"SOURCE ROW {message.Row}\nTo: {message.To}\nCC: {message.Cc}\nBCC: {message.Bcc}\nSubject: {message.Subject}\n\nPLAIN TEXT\n{message.Text}\n\nHTML SOURCE\n{message.Html}\n\nATTACHMENTS\n{string.Join("\n", message.Attachments)}\n\nVALIDATION\n{(batch.Issues.Count == 0 ? "This row is valid. Validate the full batch to check duplicates." : string.Join("\n", batch.Issues.Select(i => $"{i.Area}: {i.Description}")))}";

    }

    private async Task RunAsync()

    {

        // Always validate a fresh immutable snapshot immediately before export.

        var batch = Validate();

        if (batch.Issues.Count > 0) { SelectedTab = 2; return; }

        if (dialogs.ChooseOutput() is not { } root) return;

        var id = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8];

        OutputPath = Path.Combine(root, "MergeDesk-" + id);

        Directory.CreateDirectory(OutputPath);

        var start = DateTimeOffset.UtcNow;

        IMailSender sender = new DryRunMailSender(OutputPath);

        Results.Clear(); SelectedTab = 4; IsBusy = true;

        cancellation = new();

        try

        {

            foreach (var message in batch.Messages)

            {

                if (cancellation.IsCancellationRequested) { Results.Add(new(message.Row, message.To, message.Subject, "Cancelled", "Not exported", DateTimeOffset.Now)); continue; }

                try

                {

                    var location = await Task.Run(() => sender.SendAsync(message, cancellation.Token));

                    Results.Add(new(message.Row, message.To, message.Subject, "Exported", location, DateTimeOffset.Now));

                }

                catch (OperationCanceledException) { Results.Add(new(message.Row, message.To, message.Subject, "Cancelled", "Export interrupted; inspect partial output", DateTimeOffset.Now)); }

                catch (Exception ex) { Results.Add(new(message.Row, message.To, message.Subject, "Failed", ex.Message, DateTimeOffset.Now)); }

                Progress = $"{Results.Count:N0} / {batch.Messages.Count:N0} processed";

            }

            var run = new RunRecord(id, start, OutputPath, Results.ToArray());

            await File.WriteAllTextAsync(Path.Combine(OutputPath, "results.json"), JsonSerializer.Serialize(run, new JsonSerializerOptions { WriteIndented = true }));

            try { await repository.SaveAsync(run); }

            catch (Exception ex) { Status = $"Output saved, but SQLite history failed: {ex.Message}. See results.json."; return; }

            History.Insert(0, run);

            Status = $"Dry run finished: {Results.Count(r => r.Status == "Exported")} exported, {Results.Count(r => r.Status == "Failed")} failed, {Results.Count(r => r.Status == "Cancelled")} cancelled. No mail sent.";

        }

        finally { cancellation.Dispose(); cancellation = null; IsBusy = false; }

    }

    private async Task CreateDraftsAsync()

    {

        var creator = draftCreator;

        if (creator == null) { SelectedTab = 3; Status = "Connect an Outlook mailbox before creating drafts."; return; }

        var batch = Validate();

        if (creator is GraphDraftCreator or GraphMailSender)

            foreach (var message in batch.Messages)

                foreach (var path in message.Attachments)

                    if (File.Exists(path) && new FileInfo(path).Length > GraphDraftCreator.MaximumAttachmentBytes)

                        Issues.Add(new(message.Row, "Attachments", $"'{Path.GetFileName(path)}' exceeds the Microsoft Graph 150 MB file limit."));

        if (Issues.Count > 0) { SelectedTab = 2; Status = "Resolve all validation issues before creating drafts."; return; }

        if (!dialogs.ConfirmDraftCreation(batch.Messages.Count, creator.Destination)) { Status = "Draft creation cancelled before any mailbox changes."; return; }

        var id = "drafts-" + DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8];

        OutputPath = Path.Combine(draftLogRoot, id); Directory.CreateDirectory(OutputPath);

        var started = DateTimeOffset.UtcNow;

        Results.Clear();

        foreach (var message in batch.Messages)

            Results.Add(new(message.Row, message.To, message.Subject, "Not attempted", "Waiting", DateTimeOffset.Now, OperationId: $"{id}-row-{message.Row}"));

        var rowIndexes = batch.Messages.Select((m, index) => (m.Row, index)).ToDictionary(p => p.Row, p => p.index);

        SelectedTab = 4; IsBusy = true; cancellation = new(); Progress = $"0 / {batch.Messages.Count:N0} processed";

        RunRecord Snapshot() => new(id, started, OutputPath, Results.ToArray(), "Outlook drafts", creator.Destination);

        var journal = new DraftRunJournal(repository);

        async Task SaveJournal()

        {

            var snapshot = Snapshot();

            await journal.SaveAsync(snapshot);

        }

        var processed = 0;

        try

        {

            await SaveJournal(); // Refuse mailbox writes if audit storage is unavailable.

            Status = $"Creating drafts in {creator.Destination}. No mail will be sent.";

            await new DraftBatchRunner().CreateAsync(batch.Messages, creator, id,

                report: result => { Results[rowIndexes[result.Row]] = result; Progress = $"{++processed:N0} / {batch.Messages.Count:N0} processed"; },

                cancellationToken: cancellation.Token,

                beforeAttempt: async (message, operationId) =>

                {

                    Status = $"Saving the pre-draft log for source row {message.Row}. No mail sent.";

                    Results[rowIndexes[message.Row]] = new(message.Row, message.To, message.Subject, "Creating draft",

                        "Operation in progress. If interrupted, check this mailbox before retrying.", DateTimeOffset.Now, OperationId: operationId);

                    await SaveJournal();

                    Status = $"Creating the Outlook draft for source row {message.Row}. No mail sent.";

                },

                persist: async _ => { Status = $"Saving results: {processed:N0} / {batch.Messages.Count:N0} processed. No mail sent."; await SaveJournal(); });

            History.Insert(0, Snapshot());

            Status = $"Draft batch finished: {Results.Count(r => r.Status == "Draft created")} created, {Results.Count(r => r.Status == "Failed")} failed, {Results.Count(r => r.Status == "Needs review")} need review, {Results.Count(r => r.Status is "Cancelled" or "Not attempted")} not completed. Open Outlook's Drafts folder to review and send.";

        }

        catch (Exception ex)

        {

            for (var index = 0; index < Results.Count; index++)

            {

                var row = Results[index];

                if (row.Status == "Not attempted") Results[index] = row with { Detail = "Batch stopped while saving results. No draft attempted for this row." };

                else if (row.Status == "Creating draft") Results[index] = row with { Status = "Not attempted", Detail = "The pre-draft log could not be saved. No draft attempted for this row." };

            }

            // Keep confirmed drafts visible even if the durable result write failed.

            History.Insert(0, Snapshot());

            Status = $"Draft batch stopped: {ex.Message}. Check Outlook Drafts and the local results folder before retrying; some drafts may exist.";

        }

        finally { cancellation.Dispose(); cancellation = null; IsBusy = false; }

    }

    private async Task SendLiveAsync()

    {

        if (draftCreator is not IMailDelivery sender) { Status = "Connect an account with sending enabled first."; return; }

        var batch = Validate();

        AddSendingIssues(sender, batch);

        if (Issues.Count > 0) { SelectedTab = 2; Status = "Resolve validation issues before sending."; return; }

        if (!dialogs.ConfirmSending(batch.Messages.Count, sender.Destination, SendIntervalSeconds))

        { Status = "Sending cancelled before any mailbox changes."; return; }

        await ExecuteSendAsync(batch, sender, false);

    }

    private void AddSendingIssues(IMailDelivery sender, MergeBatch batch, bool isTest = false)
    {

        var body = string.IsNullOrWhiteSpace(Html) ? Text : Html;

        if (body.Contains("[Write your message here", StringComparison.Ordinal) || body.Contains("[Your name or team]", StringComparison.Ordinal))

            Issues.Add(new(0, "Body", "Replace the starter example text and signature before sending."));

        if (!isTest && (SendIntervalSeconds is < 1 or > 60)) Issues.Add(new(0, "Sending", "Set the send interval between 1 and 60 seconds."));
        if (sender is GraphMailSender)

            foreach (var message in batch.Messages)

                foreach (var path in message.Attachments)

                    if (File.Exists(path) && new FileInfo(path).Length > GraphDraftCreator.MaximumAttachmentBytes)

                        Issues.Add(new(message.Row, "Attachments", "Microsoft attachments must not exceed 150 MiB."));

    }

    private async Task SendTestAsync()

    {

        if (draftCreator is not IMailDelivery sender || SelectedRecipient == null) return;

        var address = TestAddress.Trim();

        Issues.Clear();

        if (!System.Net.Mail.MailAddress.TryCreate(address, out var parsed) || parsed.Address != address || !parsed.Host.Contains('.') ||

            parsed.Host.StartsWith('.') || parsed.Host.EndsWith('.') || address.IndexOfAny(['\r', '\n', ',', ';', '{', '}']) >= 0)

        { Issues.Add(new(0, "Test address", "Enter one bare email address that belongs to you, such as name@example.com.")); Status = "Enter a valid single test address. No email sent."; return; }

        // Redirect before merge validation. Neither source To, CC nor BCC can escape into the test payload.

        var template = Template() with { To = address, Cc = "", Bcc = "" };

        var batch = engine.Merge(new(data.Columns, [SelectedRecipient]), FieldMappings(), template);

        foreach (var issue in batch.Issues) Issues.Add(issue);

        AddSendingIssues(sender, batch, isTest: true);
        if (Issues.Count > 0) { SelectedTab = 2; Status = "Resolve the selected row's template and attachment issues before sending a test."; return; }
        var message = batch.Messages.Single() with { Subject = "[TEST] " + batch.Messages.Single().Subject };

        if (!dialogs.ConfirmTestSending(message, sender.Destination)) { Status = "Test email cancelled before any mailbox changes."; return; }

        await ExecuteSendAsync(new([message], []), sender, true);

    }

    private async Task ExecuteSendAsync(MergeBatch batch, IMailDelivery sender, bool isTest)
    {
        await FlushAutosaveAsync();
        var id = (isTest ? "test-" : "send-") + DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8];

        OutputPath = Path.Combine(draftLogRoot, id); var started = DateTimeOffset.UtcNow;

        Results.Clear();

        foreach (var message in batch.Messages) Results.Add(new(message.Row, message.To, message.Subject, "Not attempted", "Waiting", DateTimeOffset.Now));

        var indexes = batch.Messages.Select((m, i) => (m.Row, i)).ToDictionary(x => x.Row, x => x.i);

        RunRecord Snapshot() => new(id, started, OutputPath, Results.ToArray(), isTest ? "Test email" : "Live send", sender.Destination);

        var journal = new DraftRunJournal(repository);

        SelectedTab = 4; IsBusy = true; cancellation = new(); Progress = $"0 / {batch.Messages.Count} processed";

        try

        {

            await journal.SaveAsync(Snapshot());

            Status = isTest ? "Sending one test email to the test address only." : "Preparing and sending messages one at a time. Cancel stops future submissions.";

            await new LiveSendRunner(isTest ? testLedger : sendLedger).RunAsync(batch.Messages, sender, id, isTest ? 0 : SendIntervalSeconds, async result =>

            {

                Results[indexes[result.Row]] = result;

                Progress = $"{indexes[result.Row] + 1} / {batch.Messages.Count} processed";

                Status = $"Source row {result.Row}: {result.Status}. Saving results.";

                await journal.SaveAsync(Snapshot());

            }, cancellation.Token);

            History.Insert(0, Snapshot());

            Status = $"{(isTest ? "Test email finished" : "Send batch finished")}: {Results.Count(r => r.Status is "Submitted to Outlook" or "Accepted by Microsoft")} submitted, {Results.Count(r => r.Status == "Already submitted")} skipped, {Results.Count(r => r.Status == "Needs review")} need review. Check Outlook Outbox/Sent Items; submission does not confirm delivery.";

        }

        catch (Exception ex)

        {

            for (var i = 0; i < Results.Count; i++)

                if (Results[i].Detail == "Waiting") Results[i] = Results[i] with { Detail = "Batch stopped; no send attempted for this row." };

            History.Insert(0, Snapshot()); Status = "Sending stopped: " + ex.Message + ". Check Outlook and results before retrying.";

        }

        finally { cancellation.Dispose(); cancellation = null; IsBusy = false; }

    }

    private async Task SaveProjectAsync()

    {

        if (dialogs.ProjectFile(true) is not { } path) return;

        var project = ProjectSnapshot();

        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(project, new JsonSerializerOptions { WriteIndented = true }));

        Status = "Project saved. Recipient data stays in the original source file.";

    }

    private async Task OpenProjectAsync()

    {

        if (dialogs.ProjectFile(false) is not { } path) return;

        var project = JsonSerializer.Deserialize<MergeProject>(await File.ReadAllTextAsync(path)) ?? throw new InvalidDataException("Invalid project file.");

        await ApplyProjectAsync(project);

    }

    private async Task ClearHistoryAsync()
    {
        if (!dialogs.ConfirmHistoryClear()) return;
        IsBusy = true;
        try
        {
            await Task.Run(() => repository.ClearAsync());
            History.Clear(); Results.Clear(); OutputPath = ""; Progress = "";
            Status = "History cleared. Exported files, run logs, Outlook messages and duplicate-send protection are kept.";
        }
        finally { IsBusy = false; }
    }
    public async Task LoadHistoryAsync()

    {

        History.Clear(); foreach (var run in await repository.LoadAsync()) History.Add(run);

        Status = History.Count == 0 ? "No previous runs. Load a recipient file to start." : $"Loaded {History.Count} previous runs.";

    }

    public void SelectRun(RunRecord? run)

    {

        if (run == null || IsBusy) return;

        OutputPath = run.OutputPath; Results.Clear(); foreach (var result in run.Results) Results.Add(result);

    }

}
