using Claims.Contracts;

namespace Claims.Api.Messaging;

public sealed class ClaimNotificationOutbox
{
    public long Id { get; private set; }
    public Guid EventId { get; init; } = Guid.NewGuid();
    public required Guid ClaimId { get; init; }
    public required Guid RecipientUserId { get; init; }
    public required ClaimNotificationType Type { get; init; }
    public required DateTime OccurredAt { get; init; }
    public DateTime? PublishedAt { get; private set; }

    public ClaimNotificationEvent ToEvent() => new(EventId, ClaimId, RecipientUserId, Type, OccurredAt);

    public void MarkPublished(DateTime publishedAt) => PublishedAt = publishedAt;
}
