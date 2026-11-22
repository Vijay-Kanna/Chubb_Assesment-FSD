namespace Claims.Api.Domain;

public sealed class ClaimMessage
{
    public const int MaxBodyLength = 2000;

    public long Id { get; private set; }
    public required Guid ClaimId { get; init; }
    public required Guid SenderUserId { get; init; }
    public required string Body { get; init; }
    public required DateTime SentAt { get; init; }
}
