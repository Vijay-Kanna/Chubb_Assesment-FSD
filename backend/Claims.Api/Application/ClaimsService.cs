using Claims.Api.Domain;
using Claims.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Claims.Api.Application;

internal sealed class ClaimsService(ClaimsDbContext db, TimeProvider clock)
{
    public async Task<ClaimView> SubmitAsync(Guid claimantId, ClaimType type, DateOnly incidentDate,
        string description, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var claim = Claim.Submit(claimantId, type, incidentDate, description, now);
        db.Claims.Add(claim);
        AddHistory(claim, claimantId, ClaimAction.Submitted, previousStatus: null, now);
        await db.SaveChangesAsync(cancellationToken);
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
            .OrderBy(entry => entry.Id)
            .Select(entry => new HistoryView(entry.Id, entry.ActorUserId, entry.Action, entry.PreviousStatus,
                entry.NewStatus, entry.OccurredAt))
            .ToListAsync(cancellationToken);

        // Every submitted claim has a history entry, committed with the claim.
        return entries.Count == 0 ? null : entries;
    }

    public async Task<(PickupOutcome Outcome, ClaimView? Claim)> PickUpAsync(Guid id, Guid staffId,
        CancellationToken cancellationToken)
    {
        var claim = await db.Claims.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (claim is null)
            return (PickupOutcome.NotFound, null);

        var now = clock.GetUtcNow().UtcDateTime;
        var previousStatus = claim.Status;
        if (!claim.TryPickUp(staffId, now))
            return (PickupOutcome.Conflict, null);

        AddHistory(claim, staffId, ClaimAction.Assigned, previousStatus, now);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return (PickupOutcome.Conflict, null);
        }

        return (PickupOutcome.Assigned, ClaimView.From(claim));
    }

    private IQueryable<Claim> VisibleClaims(Guid userId, bool isStaff)
    {
        var claims = db.Claims.AsNoTracking();
        return isStaff
            ? claims.Where(claim => claim.AssignedStaffId == userId || claim.AssignedStaffId == null)
            : claims.Where(claim => claim.ClaimantId == userId);
    }

    private void AddHistory(Claim claim, Guid actorId, ClaimAction action, ClaimStatus? previousStatus, DateTime now)
    {
        db.ClaimHistory.Add(new ClaimHistory
        {
            ClaimId = claim.Id,
            ActorUserId = actorId,
            Action = action,
            PreviousStatus = previousStatus,
            NewStatus = claim.Status,
            OccurredAt = now
        });
    }
}

internal enum PickupOutcome { Assigned, NotFound, Conflict }

internal sealed record ClaimView(Guid Id, Guid ClaimantId, Guid? AssignedStaffId, ClaimType Type,
    DateOnly IncidentDate, string Description, ClaimStatus Status, DateTime CreatedAt, DateTime UpdatedAt)
{
    public static ClaimView From(Claim claim) => new(claim.Id, claim.ClaimantId, claim.AssignedStaffId, claim.Type,
        claim.IncidentDate, claim.Description, claim.Status, claim.CreatedAt, claim.UpdatedAt);
}

internal sealed record HistoryView(long Id, Guid ActorUserId, ClaimAction Action, ClaimStatus? PreviousStatus,
    ClaimStatus NewStatus, DateTime OccurredAt);
