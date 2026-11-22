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
            .WithTags("Claims")
            .RequireAuthorization(policy => policy.RequireRole(Role.Claimant, Role.ClaimsStaff));

        claims.MapPost("", Submit).RequireAuthorization(Role.Claimant)
            .Produces<ClaimView>(StatusCodes.Status201Created).ProducesValidationProblem();
        claims.MapGet("", List).Produces<ClaimView[]>().ProducesValidationProblem();
        claims.MapGet("/{id:guid}", Get).Produces<ClaimView>().Produces(StatusCodes.Status404NotFound);
        claims.MapGet("/{id:guid}/history", History).Produces<HistoryView[]>().Produces(StatusCodes.Status404NotFound);
        claims.MapGet("/{id:guid}/messages", Messages).Produces<ClaimMessageView[]>().Produces(StatusCodes.Status404NotFound);
        claims.MapGet("/{id:guid}/exposure", GetExposure).RequireAuthorization(Role.ClaimsStaff)
            .WithSummary("Read the assigned staff member's exposure estimate")
            .Produces<ExposureEstimate>().Produces(StatusCodes.Status404NotFound);
        claims.MapPut("/{id:guid}/exposure", SetExposure).RequireAuthorization(Role.ClaimsStaff)
            .WithSummary("Record the outstanding exposure estimate")
            .Produces<ClaimView>().ProducesValidationProblem()
            .Produces(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);
        claims.MapPost("/{id:guid}/pick-up", PickUp).RequireAuthorization(Role.ClaimsStaff)
            .Produces<ClaimView>().Produces(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);
        MapReviewMessage(claims, "/{id:guid}/questions", Role.ClaimsStaff, ClaimAction.QuestionAsked);
        MapReviewMessage(claims, "/{id:guid}/replies", Role.Claimant, ClaimAction.ReplyReceived);
        MapReviewAction(claims, "/{id:guid}/approve", ClaimAction.Approved);
        MapReviewAction(claims, "/{id:guid}/reject", ClaimAction.Rejected);
        MapReviewAction(claims, "/{id:guid}/settle", ClaimAction.Settled);
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
        var change = await service.PickUpAsync(id, UserId(user), cancellationToken);
        return ChangeResult(change, "Claim is no longer available for pickup.");
    }

    private static async Task<IResult> Messages(Guid id, ClaimsPrincipal user, ClaimsService service,
        CancellationToken cancellationToken)
    {
        var messages = await service.MessagesAsync(id, UserId(user), user.IsInRole(Role.ClaimsStaff), cancellationToken);
        return messages is null ? Results.NotFound() : Results.Ok(messages);
    }

    private static async Task<IResult> GetExposure(Guid id, ClaimsPrincipal user, ClaimsService service,
        CancellationToken cancellationToken)
    {
        var estimate = await service.GetExposureAsync(id, UserId(user), cancellationToken);
        return estimate is null ? Results.NotFound() : Results.Ok(estimate);
    }

    private static async Task<IResult> SetExposure(Guid id, SetExposureRequest request, ClaimsPrincipal user,
        ClaimsService service, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.Amount is not { } amount || amount is < 0 or > 1_000_000_000 ||
            decimal.Round(amount, 2) != amount)
        {
            errors["amount"] = ["Enter an amount from 0 to 1,000,000,000 with at most two decimal places."];
        }
        if (request.Currency is not { } currency || !Enum.IsDefined(currency))
            errors["currency"] = ["Select a supported currency."];
        if (errors.Count != 0)
            return Results.ValidationProblem(errors);

        var change = await service.SetExposureAsync(id, UserId(user), (long)(request.Amount!.Value * 100),
            request.Currency!.Value, cancellationToken);
        return ChangeResult(change, "An exposure estimate cannot be updated for a closed claim.");
    }

    private static void MapReviewMessage(RouteGroupBuilder claims, string route, string role, ClaimAction action)
    {
        claims.MapPost(route, async (Guid id, ReviewMessage request, ClaimsPrincipal user, ClaimsService service,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.Body) || request.Body.Length > ClaimMessage.MaxBodyLength)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["body"] = [$"Enter a message of up to {ClaimMessage.MaxBodyLength} characters."]
                });
            }

            var change = await service.ReviewAsync(id, UserId(user), action, request.Body.Trim(), cancellationToken);
            return ChangeResult(change, ReviewConflict);
        }).RequireAuthorization(role).Produces<ClaimView>().ProducesValidationProblem()
            .Produces(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static void MapReviewAction(RouteGroupBuilder claims, string route, ClaimAction action)
    {
        claims.MapPost(route, async (Guid id, ClaimsPrincipal user, ClaimsService service,
            CancellationToken cancellationToken) =>
        {
            var change = await service.ReviewAsync(id, UserId(user), action, body: null, cancellationToken);
            return ChangeResult(change, ReviewConflict);
        }).RequireAuthorization(Role.ClaimsStaff).Produces<ClaimView>()
            .Produces(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);
    }

    private const string ReviewConflict = "This action is unavailable for the current claim status.";

    private static IResult ChangeResult(ClaimChange change, string conflictTitle) => change.Outcome switch
    {
        ClaimChangeOutcome.Changed => Results.Ok(change.Claim),
        ClaimChangeOutcome.NotFound => Results.NotFound(),
        _ => Results.Problem(statusCode: StatusCodes.Status409Conflict, title: conflictTitle)
    };

    private static Guid UserId(ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue(JwtRegisteredClaimNames.Sub)!);

    private sealed record SubmitClaim(ClaimType? Type, DateOnly? IncidentDate, string? Description);
    private sealed record ReviewMessage(string? Body);
    private sealed record SetExposureRequest(decimal? Amount, ExposureCurrency? Currency);
}
