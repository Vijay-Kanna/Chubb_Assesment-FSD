namespace Claims.Api.Domain;

public enum ClaimAction
{
    Submitted, Assigned, QuestionAsked, ReplyReceived, Approved, Rejected, Settled, ExposureUpdated, RejectionReasonRecorded
}

public sealed class ClaimHistory
{
    public long Id { get; private set; }
    public required Guid ClaimId { get; init; }
    public required Guid ActorUserId { get; init; }
    public required ClaimAction Action { get; init; }
    public ClaimStatus? PreviousStatus { get; init; }
    public required ClaimStatus NewStatus { get; init; }
    public required DateTime OccurredAt { get; init; }
    public ClaimMessage? ClaimMessage { get; init; }
    public long? ClaimMessageId { get; private set; }
}
