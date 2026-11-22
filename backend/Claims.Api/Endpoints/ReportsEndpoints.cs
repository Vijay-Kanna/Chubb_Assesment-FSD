using Claims.Api.Application;
using Claims.Api.Domain;

namespace Claims.Api.Endpoints;

internal static class ReportsEndpoints
{
    public static void MapReports(this WebApplication app)
    {
        app.MapGet("/reports/overview", Overview)
            .WithTags("Reports")
            .WithSummary("Get aggregate workload, exposure, and staff activity")
            .RequireAuthorization(Role.ClaimsStaff)
            .Produces<ReportsOverview>()
            .ProducesValidationProblem();
    }

    private static async Task<IResult> Overview(ReportsService service, CancellationToken cancellationToken, int days = 30)
    {
        if (days is not (7 or 30 or 90))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["days"] = ["Select 7, 30, or 90 days."] });

        return Results.Ok(await service.OverviewAsync(days, cancellationToken));
    }
}
