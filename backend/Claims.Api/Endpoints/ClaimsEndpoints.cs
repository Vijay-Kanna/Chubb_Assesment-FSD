using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Claims.Api.Application;
using Claims.Api.Domain;

namespace Claims.Api.Endpoints;

internal static class ClaimsEndpoints
{
    public static void MapClaims(this WebApplication app)
    {
        var claims = app.MapGroup("/claims")
            .RequireAuthorization(policy => policy.RequireRole(Role.Claimant, Role.ClaimsStaff));

        claims.MapPost("", Submit).RequireAuthorization(Role.Claimant)
            .Produces<ClaimView>(StatusCodes.Status201Created).ProducesValidationProblem();
        claims.MapGet("", List).Produces<ClaimView[]>().ProducesValidationProblem();
        claims.MapGet("/{id:guid}", Get).Produces<ClaimView>().Produces(StatusCodes.Status404NotFound);
        claims.MapGet("/{id:guid}/history", History).Produces<HistoryView[]>().Produces(StatusCodes.Status404NotFound);
        claims.MapPost("/{id:guid}/pick-up", PickUp).RequireAuthorization(Role.ClaimsStaff)
            .Produces<ClaimView>().Produces(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static async Task<IResult> Submit(SubmitClaim request, ClaimsPrincipal user, ClaimsService service,
        TimeProvider clock, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.Type is null)
            errors["type"] = ["Select Motor or Property."];
        if (request.IncidentDate is null || request.IncidentDate.Value == DateOnly.MinValue ||
            request.IncidentDate.Value > DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime))
        {
            errors["incidentDate"] = ["Enter an incident date on or before today."];
        }
        if (string.IsNullOrWhiteSpace(request.Description) || request.Description.Length > 2000)
            errors["description"] = ["Enter a description of up to 2000 characters."];
        if (errors.Count > 0)
            return Results.ValidationProblem(errors);

        var claim = await service.SubmitAsync(UserId(user), request.Type!.Value, request.IncidentDate!.Value,
            request.Description!.Trim(), cancellationToken);
        return Results.Created($"/claims/{claim.Id}", claim);
    }

    private static async Task<IResult> List(ClaimsPrincipal user, ClaimsService service,
        CancellationToken cancellationToken, ClaimStatus? status = null, Guid? assignedStaffId = null, int page = 1)
    {
        var isStaff = user.IsInRole(Role.ClaimsStaff);
        if (page is < 1 or > 10000)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["page"] = ["Enter a page from 1 to 10000."] });
        if (!isStaff && assignedStaffId is not null)
            return Results.Forbid();
        if (status is not null && !Enum.IsDefined(status.Value))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["status"] = ["Enter a valid claim status."] });

        return Results.Ok(await service.ListAsync(UserId(user), isStaff, status, assignedStaffId, page, cancellationToken));
    }

    private static async Task<IResult> Get(Guid id, ClaimsPrincipal user, ClaimsService service,
        CancellationToken cancellationToken)
    {
        var claim = await service.GetAsync(id, UserId(user), user.IsInRole(Role.ClaimsStaff), cancellationToken);
        return claim is null ? Results.NotFound() : Results.Ok(claim);
    }

    private static async Task<IResult> History(Guid id, ClaimsPrincipal user, ClaimsService service,
        CancellationToken cancellationToken)
    {
        var history = await service.HistoryAsync(id, UserId(user), user.IsInRole(Role.ClaimsStaff), cancellationToken);
        return history is null ? Results.NotFound() : Results.Ok(history);
    }

    private static async Task<IResult> PickUp(Guid id, ClaimsPrincipal user, ClaimsService service,
        CancellationToken cancellationToken)
    {
        var (outcome, claim) = await service.PickUpAsync(id, UserId(user), cancellationToken);
        return outcome switch
        {
            PickupOutcome.Assigned => Results.Ok(claim),
            PickupOutcome.NotFound => Results.NotFound(),
            _ => Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Claim is no longer available for pickup.")
        };
    }

    private static Guid UserId(ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue(JwtRegisteredClaimNames.Sub)!);

    private sealed record SubmitClaim(ClaimType? Type, DateOnly? IncidentDate, string? Description);
}
