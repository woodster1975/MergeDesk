using System.Data;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Threading;
using MergeDesk.Core;

namespace MergeDesk.App.ViewModels;
public sealed partial class MainViewModel
{
    private readonly HashSet<int> excludedRows = [];
    private string sourceFingerprint = "", recipientSearch = "", testAddress = "";
    private string inclusionColumn = "", sourceRowColumn = "", matchColumn = "";
    private bool changingSelection, applyingProject, recoveryReady;
    private RecipientSelection? unloadedSelection;
    private RecoverySnapshot? pendingRecovery;
    private readonly IProjectRecoveryStore? recoveryStore;
    private readonly ISendLedger testLedger;
    private readonly DispatcherTimer? autosaveTimer;
    private readonly SemaphoreSlim autosaveGate = new(1);
    private long revision, savedRevision;
    private string autosaveStatus = "Autosave ready";
    public AsyncCommand IncludeShownCommand { get; private set; } = null!;
    public AsyncCommand ExcludeShownCommand { get; private set; } = null!;
    public AsyncCommand ExcludeAllCommand { get; private set; } = null!;
    public AsyncCommand SendTestCommand { get; private set; } = null!;
    public AsyncCommand RestoreRecoveryCommand { get; private set; } = null!;
    public AsyncCommand DismissRecoveryCommand { get; private set; } = null!;
    public string InclusionColumn => inclusionColumn;
    public string SourceRowColumn => sourceRowColumn;
    public string MatchColumn => matchColumn;
    public int IncludedCount => Recipients.Count - excludedRows.Count;
    public string InclusionSummary => $"{IncludedCount:N0} included · {excludedRows.Count:N0} excluded · {RecipientView?.Count ?? 0:N0} shown";
    public string RecipientSearch { get => recipientSearch; set { if (Set(ref recipientSearch, value)) ApplySearch(); } }
    public string TestAddress { get => testAddress; set { if (Set(ref testAddress, value)) WorkspaceChanged(); } }
    public bool HasPendingRecovery => pendingRecovery != null;
    public string RecoveryDescription => pendingRecovery == null ? "" : $"An autosaved workspace from {pendingRecovery.SavedAt.ToLocalTime():dd MMM yyyy HH:mm} is available. Restore your work or start fresh.";
    public string AutosaveStatus { get => autosaveStatus; private set => Set(ref autosaveStatus, value); }
    private RecipientData IncludedData() => new(data.Columns, data.Recipients.Where(r => !excludedRows.Contains(r.Row)).ToArray());
    private RecipientSelection? Selection() => unloadedSelection ?? (sourceFingerprint == "" ? null : new(sourceFingerprint, excludedRows.Order().ToArray()));
    public MergeProject ProjectSnapshot() => new(SourcePath, Worksheet, FieldMappings(), Template(), Selection(), SendIntervalSeconds, TestAddress);
    private static string Fingerprint(RecipientData input) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
    { input.Columns, Rows = input.Recipients.Select(r => new { r.Row, Values = input.Columns.Select(c => r.Values.GetValueOrDefault(c, "")).ToArray() }).ToArray() })));
    private bool BuildRecipientTable(RecipientSelection? selection)
    {
        sourceFingerprint = Fingerprint(data); unloadedSelection = null; excludedRows.Clear();
        var changedSource = selection != null && selection.SourceFingerprint != sourceFingerprint;
        if (changedSource) foreach (var row in data.Recipients) excludedRows.Add(row.Row);
        else if (selection != null) foreach (var row in data.Recipients.Where(r => selection.ExcludedRows.Contains(r.Row))) excludedRows.Add(row.Row);
        var table = new DataTable();
        string InternalName(string root) { var name = root; while (data.Columns.Contains(name, StringComparer.OrdinalIgnoreCase) || table.Columns.Contains(name)) name += "_"; return name; }
        inclusionColumn = InternalName("__MergeDeskInclude"); table.Columns.Add(inclusionColumn, typeof(bool));
        sourceRowColumn = InternalName("__MergeDeskRow"); table.Columns.Add(sourceRowColumn, typeof(int));
        matchColumn = InternalName("__MergeDeskMatch"); table.Columns.Add(matchColumn, typeof(bool));
        foreach (var column in data.Columns) table.Columns.Add(column);
        foreach (var recipient in data.Recipients)
            table.Rows.Add(new object[] { !excludedRows.Contains(recipient.Row), recipient.Row, true }.Concat(data.Columns.Select(c => (object)recipient.Values[c])).ToArray());
        table.ColumnChanged += (_, e) =>
        {
            if (changingSelection || e.Column?.ColumnName != inclusionColumn) return;
            var row = (int)e.Row[sourceRowColumn];
            if ((bool)e.Row[inclusionColumn]) excludedRows.Remove(row); else excludedRows.Add(row);
            SelectionChanged();
        };
        Changed(nameof(InclusionColumn)); Changed(nameof(SourceRowColumn)); Changed(nameof(MatchColumn));
        RecipientView = table.DefaultView; ApplySearch(); SelectionChanged(); return changedSource;
    }
    private void ApplySearch()
    {
        if (RecipientView?.Table is not { } table) return;
        changingSelection = true;
        try
        {
            var query = RecipientSearch.Trim();
            foreach (DataRow row in table.Rows)
                row[matchColumn] = query.Length == 0 || data.Columns.Any(c => ((string)row[c]).Contains(query, StringComparison.OrdinalIgnoreCase));
            RecipientView.RowFilter = $"[{matchColumn}] = true";
        }
        finally { changingSelection = false; }
        Changed(nameof(InclusionSummary)); IncludeShownCommand?.Refresh(); ExcludeShownCommand?.Refresh();
    }
    private void SetInclusion(bool include, bool all = false)
    {
        if (!IsEditable || RecipientView == null) return;
        var rows = all ? RecipientView.Table!.Rows.Cast<DataRow>().ToArray() : RecipientView.Cast<DataRowView>().Select(r => r.Row).ToArray();
        changingSelection = true;
        try
        {
            foreach (var row in rows)
            { row[inclusionColumn] = include; if (include) excludedRows.Remove((int)row[sourceRowColumn]); else excludedRows.Add((int)row[sourceRowColumn]); }
        }
        finally { changingSelection = false; }
        SelectionChanged();
    }
    private void SelectionChanged() { Changed(nameof(IncludedCount)); Changed(nameof(InclusionSummary)); Invalidate(); foreach (var command in commands) command.Refresh(); }
    private void WorkspaceChanged()
    {
        revision++;
        if (recoveryStore == null || !recoveryReady || applyingProject || HasPendingRecovery) return;
        AutosaveStatus = "Unsaved changes · autosaving shortly";
        autosaveTimer!.Stop(); if (!IsBusy) autosaveTimer.Start();
    }
    public async Task LoadRecoveryAsync()
    {
        if (recoveryStore == null) return;
        try { pendingRecovery = await recoveryStore.LoadAsync(); RecoveryChanged(); AutosaveStatus = HasPendingRecovery ? "Autosave awaiting your choice" : "Autosave ready"; }
        catch (Exception ex) { AutosaveStatus = "Recovery unavailable: " + ex.Message; }
        recoveryReady = true;
        if (!HasPendingRecovery && revision != savedRevision) autosaveTimer?.Start();
    }
    private void RecoveryChanged()
    { Changed(nameof(HasPendingRecovery)); Changed(nameof(RecoveryDescription)); Changed(nameof(IsEditable)); foreach (var command in commands) command.Refresh(); RestoreRecoveryCommand.Refresh(); DismissRecoveryCommand.Refresh(); }
    private async Task RestoreRecoveryAsync()
    {
        if (pendingRecovery == null || recoveryStore == null) return;
        await ApplyProjectAsync(pendingRecovery.Project);
        // Do not remove the old snapshot until the restored project is safely persisted in this session.
        await recoveryStore.SaveAsync(ProjectSnapshot());
        await recoveryStore.AcknowledgeAsync(); pendingRecovery = null; savedRevision = revision; RecoveryChanged();
        AutosaveStatus = "Autosaved workspace restored"; SelectedTab = 0;
    }
    private async Task DismissRecoveryAsync()
    {
        await recoveryStore!.AcknowledgeAsync(); pendingRecovery = null; RecoveryChanged();
        AutosaveStatus = "Autosave ready"; Status = "Start with a recipient file or open a saved project.";
    }
    public async Task<bool> FlushAutosaveAsync()
    {
        autosaveTimer?.Stop();
        if (recoveryStore == null || !recoveryReady || applyingProject || HasPendingRecovery || IsBusy || revision == savedRevision) return true;
        await autosaveGate.WaitAsync();
        try
        {
            if (revision == savedRevision || applyingProject || HasPendingRecovery || IsBusy) return true;
            var capturedRevision = revision; var snapshot = ProjectSnapshot();
            await recoveryStore.SaveAsync(snapshot); savedRevision = capturedRevision;
            AutosaveStatus = $"Autosaved at {DateTime.Now:HH:mm}";
            if (revision != savedRevision) autosaveTimer!.Start();
            return true;
        }
        catch (Exception ex) { AutosaveStatus = "Autosave failed — save your project. " + ex.Message; return false; }
        finally { autosaveGate.Release(); }
    }
    private async Task ApplyProjectAsync(MergeProject project)
    {
        if (project.Template == null || project.Mappings == null) throw new InvalidDataException("Project is missing its template or field mappings.");
        applyingProject = true; autosaveTimer?.Stop();
        try
        {
            Worksheet = project.Worksheet;
            string? sourceProblem = null;
            try
            {
                if (string.IsNullOrEmpty(project.SourcePath) || !File.Exists(project.SourcePath)) throw new FileNotFoundException("Source file is missing.");
                await ImportAsync(project.SourcePath, project.Mappings, project.Selection);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                sourceProblem = ex.Message; data = new([], []); SourcePath = project.SourcePath ?? ""; sourceFingerprint = ""; unloadedSelection = project.Selection;
                excludedRows.Clear(); Columns.Clear(); Mappings.Clear(); foreach (var m in project.Mappings) Mappings.Add(new(m.Field, m.Column));
                Recipients.Clear(); RecipientView = null; SelectedRecipient = null; Changed(nameof(RecipientSummary)); Changed(nameof(InclusionSummary)); Changed(nameof(IncludedCount));
            }
            To = project.Template.To; Cc = project.Template.Cc; Bcc = project.Template.Bcc; Subject = project.Template.Subject;
            Html = project.Template.Html; Text = project.Template.Text; Patterns = string.Join(Environment.NewLine, project.Template.AttachmentPatterns);
            Attachments.Clear(); foreach (var file in project.Template.GlobalAttachments) Attachments.Add(file);
            SendIntervalSeconds = project.SendIntervalSeconds; TestAddress = project.TestAddress;
            if (sourceProblem != null) Status = "Template and mappings restored, but recipients could not be loaded: " + sourceProblem + " Restore the source file and Reload, or load recipients and review the mappings.";
            WorkspaceChanged(); foreach (var command in commands) command.Refresh();
        }
        finally { applyingProject = false; if (!HasPendingRecovery && revision != savedRevision && recoveryReady) autosaveTimer?.Start(); }
    }
}
