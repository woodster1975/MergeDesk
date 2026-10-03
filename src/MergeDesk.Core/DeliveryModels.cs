namespace MergeDesk.Core;

public interface IMailDelivery : IMailDraftCreator, IMailSender
{
    string SubmissionStatus { get; }
    Task SubmitDraftAsync(string draftId, CancellationToken token = default);
}
public sealed class SubmissionRejectedException(string message, DateTimeOffset? retryAfter = null) : Exception(message)
{ public DateTimeOffset? RetryAfter { get; } = retryAfter; }
public sealed record DeliveryAttempt(string Key, string OperationId, string State, string? DraftId = null,
    string Detail = "", DateTimeOffset? RetryAfter = null);
public sealed record DeliveryReservation(DeliveryAttempt Attempt, bool Owned);
public interface ISendLedger
{
    Task<DeliveryReservation> ReserveAsync(string key, string operationId);
    Task SaveAsync(DeliveryAttempt attempt);
}
