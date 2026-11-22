using Claims.Api.Domain;
using Claims.Api.Messaging;
using Claims.Api.Persistence;
using Claims.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Claims.Api.Application;

internal sealed class ClaimsService(ClaimsDbContext db, TimeProvider clock, ClaimUpdates updates)
{
    public async Task<ClaimView> SubmitAsync(Guid claimantId, ClaimType type, DateOnly incidentDate,
        string description, CancellationToken cancellationToken, ClaimAmount? requested = null)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var claim = Claim.Submit(claimantId, type, incidentDate, description, now, requested);
        db.Claims.Add(claim);
        AddHistory(claim, claimantId, ClaimAction.Submitted, previousStatus: null, now);
        await db.SaveChangesAsync(cancellationToken);
        await updates.PublishAsync(claimantId);
        return ClaimView.From(claim);
    }

    public async Task<IReadOnlyList<ClaimView>> ListAsync(Guid userId, bool isStaff, ClaimStatus? status,
        Guid? assignedStaffId, int page, CancellationToken cancellationToken)
    {
        var query = VisibleClaims(userId, isStaff);
        if (status is not null)
            query = query.Where(claim => claim.Status == status);
        if (assignedStaffId is not null)
            query = query.Where(claim => claim.AssignedStaffId == assignedStaffId);

        var claims = await query.OrderByDescending(claim => claim.CreatedAt).ThenBy(claim => claim.Id)
            .Skip((page - 1) * 50).Take(50).ToListAsync(cancellationToken);

        return claims.Select(ClaimView.From).ToList();
    }

    public async Task<ClaimView?> GetAsync(Guid id, Guid userId, bool isStaff, CancellationToken cancellationToken)
    {
        var claim = await VisibleClaims(userId, isStaff).SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        return claim is null ? null : ClaimView.From(claim);
    }

    public async Task<IReadOnlyList<HistoryView>?> HistoryAsync(Guid id, Guid userId, bool isStaff,
        CancellationToken cancellationToken)
    {
        var authorizedClaim = VisibleClaims(userId, isStaff).Where(claim => claim.Id == id);
        var entries = await db.ClaimHistory.AsNoTracking()
            .Where(entry => authorizedClaim.Any(claim => claim.Id == entry.ClaimId))
            .Where(entry => isStaff || entry.Action != ClaimAction.ExposureUpdated)
            .OrderBy(entry => entry.Id)
            .Select(entry => new HistoryView(entry.Id, entry.ActorUserId, entry.Action, entry.PreviousStatus,
                entry.NewStatus, entry.OccurredAt, entry.ClaimMessageId))
            .ToListAsync(cancellationToken);

        // Every submitted claim has a history entry, committed with the claim.
        return entries.Count == 0 ? null : entries;
    }

    public Task<ExposureEstimate?> GetExposureAsync(Guid id, Guid staffId, CancellationToken cancellationToken) =>
        ParticipantClaims(staffId, isStaff: true).AsNoTracking().Where(claim => claim.Id == id)
            .Select(claim => new ExposureEstimate(claim.EstimatedExposureMinor, claim.EstimatedExposureCurrency))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<ClaimChange> SetExposureAsync(Guid id, Guid staffId, long amountMinor,
        ExposureCurrency currency, CancellationToken cancellationToken)
    {
        var claim = await ParticipantClaims(staffId, isStaff: true)
            .SingleOrDefaultAsync(claim => claim.Id == id, cancellationToken);
        if (claim is null)
            return new(ClaimChangeOutcome.NotFound);

        var version = claim.Version;
        var now = clock.GetUtcNow().UtcDateTime;
        if (!claim.TrySetExposure(amountMinor, currency, now))
            return new(ClaimChangeOutcome.Conflict);
        if (claim.Version == version)
            return new(ClaimChangeOutcome.Changed, ClaimView.From(claim));

        AddHistory(claim, staffId, ClaimAction.ExposureUpdated, claim.Status, now);
        return await SaveChangeAsync(claim, cancellationToken);
    }

    public async Task<IReadOnlyList<ClaimMessageView>?> MessagesAsync(Guid id, Guid userId, bool isStaff,
        CancellationToken cancellationToken)
    {
        return await ParticipantClaims(userId, isStaff).AsNoTracking()
            .Where(claim => claim.Id == id)
            .Select(claim => db.ClaimMessages.Where(message => message.ClaimId == claim.Id)
                .OrderBy(message => message.Id)
                .Select(message => new ClaimMessageView(message.Id, message.SenderUserId, message.Body, message.SentAt))
                .ToList())
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<ClaimChange> PickUpAsync(Guid id, Guid staffId,
        CancellationToken cancellationToken)
    {
        var claim = await db.Claims.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (claim is null)
            return new(ClaimChangeOutcome.NotFound);

        var now = clock.GetUtcNow().UtcDateTime;
        var previousStatus = claim.Status;
        if (!claim.TryPickUp(staffId, now))
            return new(ClaimChangeOutcome.Conflict);

        AddHistory(claim, staffId, ClaimAction.Assigned, previousStatus, now);
        return await SaveChangeAsync(claim, cancellationToken);
    }

    public async Task<ClaimChange> ReviewAsync(Guid id, Guid actorId, ClaimAction action, string? body,
        CancellationToken cancellationToken, ClaimAmount? approved = null)
    {
        var claim = await ParticipantClaims(actorId, isStaff: action != ClaimAction.ReplyReceived)
            .SingleOrDefaultAsync(claim => claim.Id == id, cancellationToken);
        if (claim is null)
            return new(ClaimChangeOutcome.NotFound);

        var now = clock.GetUtcNow().UtcDateTime;
        var previousStatus = claim.Status;
        var changed = action switch
        {
            ClaimAction.QuestionAsked => claim.TryAskQuestion(now),
            ClaimAction.ReplyReceived => claim.TryReply(now),
            ClaimAction.Approved => claim.TryApprove(now, approved),
            ClaimAction.Rejected => body is not null && claim.TryReject(body, now),
            ClaimAction.Settled => claim.TrySettle(now),
            ClaimAction.RejectionReasonRecorded => body is not null && claim.TryRecordMissingRejectionReason(body, now),
            _ => throw new ArgumentOutOfRangeException(nameof(action))
        };
        if (!changed)
            return new(ClaimChangeOutcome.Conflict);

        var message = action is ClaimAction.QuestionAsked or ClaimAction.ReplyReceived ? new ClaimMessage
        {
            ClaimId = id,
            SenderUserId = actorId,
            Body = body!,
            SentAt = now
        } : null;
        AddHistory(claim, actorId, action, previousStatus, now, message);
        QueueNotification(claim, action, now);
        return await SaveChangeAsync(claim, cancellationToken);
    }

    private void QueueNotification(Claim claim, ClaimAction action, DateTime now)
    {
        ClaimNotificationType? type = action switch
        {
            ClaimAction.QuestionAsked => ClaimNotificationType.QuestionAsked,
            ClaimAction.Approved => ClaimNotificationType.Approved,
            ClaimAction.Rejected => ClaimNotificationType.Rejected,
            ClaimAction.Settled => ClaimNotificationType.Settled,
            _ => null
        };
        if (type is not null)
        {
            db.ClaimNotificationOutbox.Add(new ClaimNotificationOutbox
            {
                ClaimId = claim.Id,
                RecipientUserId = claim.ClaimantId,
                Type = type.Value,
                OccurredAt = now
            });
        }
    }

    private async Task<ClaimChange> SaveChangeAsync(Claim claim, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return new(ClaimChangeOutcome.Conflict);
        }

        await updates.PublishAsync(claim.ClaimantId);
        return new(ClaimChangeOutcome.Changed, ClaimView.From(claim));
    }

    private IQueryable<Claim> VisibleClaims(Guid userId, bool isStaff)
    {
        var claims = db.Claims.AsNoTracking();
        return isStaff
            ? claims.Where(claim => claim.AssignedStaffId == userId || claim.AssignedStaffId == null)
            : claims.Where(claim => claim.ClaimantId == userId);
    }

    private IQueryable<Claim> ParticipantClaims(Guid userId, bool isStaff) => isStaff
        ? db.Claims.Where(claim => claim.AssignedStaffId == userId)
        : db.Claims.Where(claim => claim.ClaimantId == userId);

    private void AddHistory(Claim claim, Guid actorId, ClaimAction action, ClaimStatus? previousStatus, DateTime now,
        ClaimMessage? message = null)
    {
        db.ClaimHistory.Add(new ClaimHistory
        {
            ClaimId = claim.Id,
            ActorUserId = actorId,
            Action = action,
            PreviousStatus = previousStatus,
            NewStatus = claim.Status,
            OccurredAt = now,
            ClaimMessage = message
        });
    }
}

internal enum ClaimChangeOutcome { Changed, NotFound, Conflict }

internal sealed record ClaimChange(ClaimChangeOutcome Outcome, ClaimView? Claim = null);

internal sealed record ClaimView(Guid Id, Guid ClaimantId, Guid? AssignedStaffId, ClaimType Type,
    DateOnly IncidentDate, string Description, ClaimStatus Status, DateTime CreatedAt, DateTime UpdatedAt,
    string? RejectionReason, long? RequestedAmountMinor, ExposureCurrency? RequestedCurrency,
    long? ApprovedAmountMinor, ExposureCurrency? ApprovedCurrency)
{
    public static ClaimView From(Claim claim) => new(claim.Id, claim.ClaimantId, claim.AssignedStaffId, claim.Type,
        claim.IncidentDate, claim.Description, claim.Status, claim.CreatedAt, claim.UpdatedAt, claim.RejectionReason,
        claim.RequestedAmountMinor, claim.RequestedCurrency, claim.ApprovedAmountMinor, claim.ApprovedCurrency);
}

internal sealed record HistoryView(long Id, Guid ActorUserId, ClaimAction Action, ClaimStatus? PreviousStatus,
    ClaimStatus NewStatus, DateTime OccurredAt, long? ClaimMessageId);

internal sealed record ClaimMessageView(long Id, Guid SenderUserId, string Body, DateTime SentAt);

internal sealed record ExposureEstimate(long? AmountMinor, ExposureCurrency? Currency);
