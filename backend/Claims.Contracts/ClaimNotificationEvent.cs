using System.Text.Json;
using System.Text.Json.Serialization;

namespace Claims.Contracts;

[JsonConverter(typeof(JsonStringEnumConverter<ClaimNotificationType>))]
public enum ClaimNotificationType
{
    QuestionAsked = 1,
    Approved,
    Rejected,
    Settled
}

public sealed record ClaimNotificationEvent(Guid EventId, Guid ClaimId, Guid RecipientUserId,
    ClaimNotificationType Type, DateTime OccurredAt)
{
    public const string DefaultTopic = "claim-notifications.v1";
    public static JsonSerializerOptions Json { get; } = new(JsonSerializerDefaults.Web);
}
