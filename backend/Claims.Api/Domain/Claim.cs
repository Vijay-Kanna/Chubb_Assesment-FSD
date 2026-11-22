namespace Claims.Api.Domain;

public enum ClaimStatus { Submitted, UnderReview }
public enum ClaimType { Motor, Property }

public sealed class Claim
{
    private Claim() { }

    public Guid Id { get; private set; }
    public Guid ClaimantId { get; private set; }
    public Guid? AssignedStaffId { get; private set; }
    public ClaimType Type { get; private set; }
    public DateOnly IncidentDate { get; private set; }
    public string Description { get; private set; } = string.Empty;
    public ClaimStatus Status { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }
    public int Version { get; private set; }

    public static Claim Submit(Guid claimantId, ClaimType type, DateOnly incidentDate, string description, DateTime now) =>
        new()
        {
            Id = Guid.NewGuid(),
            ClaimantId = claimantId,
            Type = type,
            IncidentDate = incidentDate,
            Description = description,
            Status = ClaimStatus.Submitted,
            CreatedAt = now,
            UpdatedAt = now
        };

    public bool TryPickUp(Guid staffId, DateTime now)
    {
        if (Status != ClaimStatus.Submitted || AssignedStaffId is not null)
            return false;

        AssignedStaffId = staffId;
        Status = ClaimStatus.UnderReview;
        UpdatedAt = now;
        Version++;
        return true;
    }
}
