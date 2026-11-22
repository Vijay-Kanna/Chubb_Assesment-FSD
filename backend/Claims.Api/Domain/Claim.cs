namespace Claims.Api.Domain;

public enum ClaimStatus { Submitted, UnderReview, AwaitingInformation, Approved, Rejected, Settled }
public enum ClaimType { Motor, Property }
public enum ExposureCurrency { MYR, SGD, AUD, HKD, NZD, USD }

public sealed class Claim
{
    public const int MaxRejectionReasonLength = 1000;

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
    public long? EstimatedExposureMinor { get; private set; }
    public ExposureCurrency? EstimatedExposureCurrency { get; private set; }
    public string? RejectionReason { get; private set; }

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
        MoveTo(ClaimStatus.UnderReview, now);
        return true;
    }

    public bool TryAskQuestion(DateTime now) => TryMove(ClaimStatus.UnderReview, ClaimStatus.AwaitingInformation, now);

    public bool TryReply(DateTime now) => TryMove(ClaimStatus.AwaitingInformation, ClaimStatus.UnderReview, now);

    public bool TryApprove(DateTime now) => TryMove(ClaimStatus.UnderReview, ClaimStatus.Approved, now);

    public bool TryReject(string reason, DateTime now)
    {
        if (!TryMove(ClaimStatus.UnderReview, ClaimStatus.Rejected, now))
            return false;

        RejectionReason = reason;
        return true;
    }

    public bool TryRecordMissingRejectionReason(string reason, DateTime now)
    {
        if (Status != ClaimStatus.Rejected || RejectionReason is not null)
            return false;

        RejectionReason = reason;
        UpdateVersion(now);
        return true;
    }

    public bool TrySettle(DateTime now) => TryMove(ClaimStatus.Approved, ClaimStatus.Settled, now);

    public bool TrySetExposure(long amountMinor, ExposureCurrency currency, DateTime now)
    {
        if (AssignedStaffId is null || Status is ClaimStatus.Rejected or ClaimStatus.Settled)
            return false;
        if (EstimatedExposureMinor == amountMinor && EstimatedExposureCurrency == currency)
            return true;

        EstimatedExposureMinor = amountMinor;
        EstimatedExposureCurrency = currency;
        UpdateVersion(now);
        return true;
    }

    private bool TryMove(ClaimStatus requiredStatus, ClaimStatus nextStatus, DateTime now)
    {
        if (Status != requiredStatus)
            return false;

        MoveTo(nextStatus, now);
        return true;
    }

    private void MoveTo(ClaimStatus status, DateTime now)
    {
        Status = status;
        UpdateVersion(now);
    }

    private void UpdateVersion(DateTime now)
    {
        UpdatedAt = now;
        Version++;
    }
}
