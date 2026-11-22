using Claims.Api.Domain;
using Claims.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Claims.Api.Application;

internal sealed class ReportsService(ClaimsDbContext db, TimeProvider clock)
{
    public async Task<ReportsOverview> OverviewAsync(int days, DateOnly? startDate, DateOnly? endDate,
        CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var since = startDate?.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc) ?? now.AddDays(-days);
        var until = endDate?.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc) ?? now;
        var claims = db.Claims.AsNoTracking();
        if (startDate is not null)
            claims = claims.Where(claim => claim.CreatedAt >= since && claim.CreatedAt <= until);

        var groups = await claims
            .GroupBy(claim => new { claim.Status, claim.AssignedStaffId, claim.EstimatedExposureCurrency })
            .Select(group => new
            {
                group.Key.Status,
                group.Key.AssignedStaffId,
                Currency = group.Key.EstimatedExposureCurrency,
                Count = group.Count(),
                EstimatedClaims = group.Count(claim => claim.EstimatedExposureMinor.HasValue),
                AmountMinor = group.Sum(claim => claim.EstimatedExposureMinor ?? 0)
            })
            .ToListAsync(cancellationToken);
        var counts = groups.GroupBy(row => row.Status).ToDictionary(group => group.Key, group => group.Sum(row => row.Count));
        var open = groups.Where(row => row.Status is not (ClaimStatus.Settled or ClaimStatus.Rejected)).ToArray();
        var exposure = open.Where(row => row.Currency.HasValue && row.EstimatedClaims > 0)
            .GroupBy(row => row.Currency!.Value)
            .Select(group => new ExposureTotal(group.Key, group.Sum(row => row.AmountMinor), group.Sum(row => row.EstimatedClaims)))
            .OrderBy(row => row.Currency.ToString(), StringComparer.Ordinal).ToArray();

        var requested = await claims.Where(claim => claim.RequestedCurrency.HasValue && claim.RequestedAmountMinor.HasValue)
            .GroupBy(claim => claim.RequestedCurrency!.Value)
            .Select(group => new ExposureTotal(group.Key, group.Sum(claim => claim.RequestedAmountMinor!.Value), group.Count()))
            .ToArrayAsync(cancellationToken);
        var approved = await claims.Where(claim => claim.ApprovedCurrency.HasValue && claim.ApprovedAmountMinor.HasValue)
            .GroupBy(claim => claim.ApprovedCurrency!.Value)
            .Select(group => new ExposureTotal(group.Key, group.Sum(claim => claim.ApprovedAmountMinor!.Value), group.Count()))
            .ToArrayAsync(cancellationToken);
        var history = db.ClaimHistory.AsNoTracking();
        if (startDate is not null)
            history = history.Where(entry => claims.Any(claim => claim.Id == entry.ClaimId));

        var activity = await history
            .Where(entry => entry.OccurredAt >= since && entry.OccurredAt <= until &&
                (entry.Action == ClaimAction.Approved || entry.Action == ClaimAction.Rejected || entry.Action == ClaimAction.Settled))
            .GroupBy(entry => entry.ActorUserId)
            .Select(group => new
            {
                Id = group.Key,
                Decisions = group.Count(entry => entry.Action != ClaimAction.Settled),
                Settlements = group.Count(entry => entry.Action == ClaimAction.Settled)
            })
            .ToDictionaryAsync(row => row.Id, cancellationToken);
        var reviewStaff = await db.Users.AsNoTracking()
            .Where(user => user.RoleId == Role.ClaimsStaff).OrderBy(user => user.Name)
            .Select(user => new { user.Id, user.Name }).ToListAsync(cancellationToken);

        var assignedWorkload = open.Where(row => row.AssignedStaffId.HasValue)
            .GroupBy(row => row.AssignedStaffId!.Value)
            .ToDictionary(group => group.Key, group => group.Sum(row => row.Count));
        var staff = reviewStaff.Select(user =>
        {
            var reviews = activity.GetValueOrDefault(user.Id);
            return new StaffActivity(user.Name, assignedWorkload.GetValueOrDefault(user.Id),
                reviews?.Decisions ?? 0, reviews?.Settlements ?? 0);
        }).ToArray();

        var totalOpen = open.Sum(row => row.Count);
        return new ReportsOverview(now, days, totalOpen, open.Where(row => row.AssignedStaffId is null).Sum(row => row.Count),
            counts.GetValueOrDefault(ClaimStatus.AwaitingInformation), totalOpen - exposure.Sum(row => row.Claims),
            Enum.GetValues<ClaimStatus>().Select(status => new StatusCount(status, counts.GetValueOrDefault(status))).ToArray(),
            exposure, staff, groups.Sum(row => row.Count), startDate, endDate, SortByCurrency(requested), SortByCurrency(approved));
    }

    private static ExposureTotal[] SortByCurrency(IEnumerable<ExposureTotal> totals) =>
        totals.OrderBy(row => row.Currency.ToString(), StringComparer.Ordinal).ToArray();
}

internal sealed record ReportsOverview(DateTime GeneratedAt, int Days, int OpenClaims, int Unassigned,
    int AwaitingInformation, int MissingExposure, IReadOnlyList<StatusCount> Statuses,
    IReadOnlyList<ExposureTotal> Exposure, IReadOnlyList<StaffActivity> Staff, int TotalClaims,
    DateOnly? StartDate, DateOnly? EndDate, IReadOnlyList<ExposureTotal> Requested, IReadOnlyList<ExposureTotal> Approved);
internal sealed record StatusCount(ClaimStatus Status, int Count);
internal sealed record ExposureTotal(ExposureCurrency Currency, long AmountMinor, int Claims);
internal sealed record StaffActivity(string Name, int OpenClaims, int Decisions, int Settlements);
