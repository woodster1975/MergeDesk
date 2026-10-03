namespace MergeDesk.Core;

public sealed record Recipient(int Row, Dictionary<string, string> Values);
public sealed record RecipientData(string[] Columns, IReadOnlyList<Recipient> Recipients);
public sealed record FieldMapping(string Field, string Column);
public sealed record MergeTemplate(string To, string Cc, string Bcc, string Subject, string Html, string Text,
    IReadOnlyList<string> GlobalAttachments, IReadOnlyList<string> AttachmentPatterns);
public sealed record RecipientSelection(string SourceFingerprint, int[] ExcludedRows);
public sealed record MergeProject(string? SourcePath, int Worksheet, FieldMapping[] Mappings, MergeTemplate Template,
    RecipientSelection? Selection = null, int SendIntervalSeconds = 3, string TestAddress = "");
public sealed record RecoverySnapshot(DateTimeOffset SavedAt, MergeProject Project, int Version = 1);
public interface IProjectRecoveryStore
{
    Task<RecoverySnapshot?> LoadAsync();
    Task SaveAsync(MergeProject project);
    Task AcknowledgeAsync();
}
public sealed record MergedMessage(int Row, string To, string Cc, string Bcc, string Subject, string Html, string Text,
    IReadOnlyList<string> Attachments, IReadOnlyList<InlineImage>? InlineImages = null);
public sealed record InlineImage(string ContentId, string Name, string MediaType, byte[] Bytes);
public sealed record ValidationIssue(int Row, string Area, string Description);
public sealed record MergeBatch(IReadOnlyList<MergedMessage> Messages, IReadOnlyList<ValidationIssue> Issues);
public sealed record RunResult(int Row, string Recipient, string Subject, string Status, string Detail, DateTimeOffset Time,
    string? DraftId = null, string? OperationId = null);
public sealed record RunRecord(string Id, DateTimeOffset Started, string OutputPath, IReadOnlyList<RunResult> Results,
    string Mode = "Dry run", string Destination = "");
public sealed record DraftAccount(string Key, string Name, string Email)
{
    public string DisplayName => $"{Name} ({Email})";
}
public sealed record DraftReceipt(string Id, string Location);
public interface IMailDraftCreator
{
    string Destination { get; }
    Task<DraftReceipt> CreateDraftAsync(MergedMessage message, string operationId, CancellationToken cancellationToken = default);
}
/// <summary>A failed operation which may have left a draft requiring manual review.</summary>
public sealed class DraftCreationException(string message, string? draftId = null, bool mayExist = false, Exception? inner = null)
    : Exception(message, inner)
{
    public string? DraftId { get; } = draftId;
    public bool MayExist { get; } = mayExist || draftId != null;
}
public interface IRecipientReader { RecipientData Read(string path, int worksheet = 1); }
public interface IMailSender
{
    Task<string> SendAsync(MergedMessage message, CancellationToken cancellationToken = default);
}
public interface IRunRepository
{
    Task ClearAsync(CancellationToken cancellationToken = default) => Task.FromException(new NotSupportedException("History clearing is unavailable for this repository."));
    Task SaveAsync(RunRecord run, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RunRecord>> LoadAsync(CancellationToken cancellationToken = default);
}
