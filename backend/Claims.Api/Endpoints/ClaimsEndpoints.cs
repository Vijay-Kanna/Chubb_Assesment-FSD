using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Claims.Api.Application;
using Claims.Api.Domain;
using Claims.Contracts;
using Claim = Claims.Api.Domain.Claim;

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
        claims.MapGet("", List).WithDescription("Set pageSize to receive the matching record count in X-Total-Count.")
            .Produces<ClaimView[]>().ProducesValidationProblem();
        claims.MapGet("/{id:guid}", Get).Produces<ClaimView>().Produces(StatusCodes.Status404NotFound);
        claims.MapGet("/{id:guid}/history", History).WithDescription("Pass page or pageSize for latest-first pages and X-Total-Count.")
            .Produces<HistoryView[]>().ProducesValidationProblem().Produces(StatusCodes.Status404NotFound);
        claims.MapGet("/{id:guid}/messages", Messages).WithDescription("Pass page or pageSize for latest-first pages and X-Total-Count. Messages are chronological within each page. Use throughMessageId to keep an older page stable.")
            .Produces<ClaimMessageView[]>().ProducesValidationProblem().Produces(StatusCodes.Status404NotFound);
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
        claims.MapPost("/{id:guid}/approve", Approve).RequireAuthorization(Role.ClaimsStaff)
            .Produces<ClaimView>().ProducesValidationProblem()
            .Produces(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);
        MapRejection(claims, HttpMethods.Post, "/{id:guid}/reject", ClaimAction.Rejected,
            "Reject a claim with a claimant-facing explanation");
        MapRejection(claims, HttpMethods.Put, "/{id:guid}/rejection-reason", ClaimAction.RejectionReasonRecorded,
            "Record the missing explanation for a previously rejected claim");
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
        var requested = ReadAmount(request.RequestedAmount, request.RequestedCurrency, errors, "requestedAmount", "requestedCurrency");
        if (errors.Count > 0)
            return Results.ValidationProblem(errors);

        var claim = await service.SubmitAsync(UserId(user), request.Type!.Value, request.IncidentDate!.Value,
            request.Description!.Trim(), cancellationToken, requested);
        return Results.Created($"/claims/{claim.Id}", claim);
    }

    private static async Task<IResult> List(HttpContext context, ClaimsPrincipal user, ClaimsService service,
        CancellationToken cancellationToken, ClaimStatus? status = null, Guid? assignedStaffId = null, int page = 1,
        int? pageSize = null)
    {
        var isStaff = user.IsInRole(Role.ClaimsStaff);
        var window = new PageWindow(page, pageSize ?? PageWindow.DefaultSize);
        if (!window.IsValid)
            return Results.ValidationProblem(window.ValidationErrors());
        if (!isStaff && assignedStaffId is not null)
            return Results.Forbid();
        if (status is not null && !Enum.IsDefined(status.Value))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["status"] = ["Enter a valid claim status."] });

        var claims = await service.ListAsync(UserId(user), isStaff, status, assignedStaffId, window,
            pageSize.HasValue, cancellationToken);
        return PageResult(context, claims);
    }

    private static async Task<IResult> Get(Guid id, ClaimsPrincipal user, ClaimsService service,
        CancellationToken cancellationToken)
    {
        var claim = await service.GetAsync(id, UserId(user), user.IsInRole(Role.ClaimsStaff), cancellationToken);
        return claim is null ? Results.NotFound() : Results.Ok(claim);
    }

    private static async Task<IResult> History(Guid id, HttpContext context, ClaimsPrincipal user, ClaimsService service,
        CancellationToken cancellationToken, int? page = null, int? pageSize = null)
    {
        var window = OptionalPage(page, pageSize);
        if (window is { IsValid: false } invalid)
            return Results.ValidationProblem(invalid.ValidationErrors());

        var history = await service.HistoryAsync(id, UserId(user), user.IsInRole(Role.ClaimsStaff), window, cancellationToken);
        return PageResult(context, history);
    }

    private static async Task<IResult> PickUp(Guid id, ClaimsPrincipal user, ClaimsService service,
        CancellationToken cancellationToken)
    {
        var change = await service.PickUpAsync(id, UserId(user), cancellationToken);
        return ChangeResult(change, "This claim has already been picked up by a staff member and cannot be taken again.");
    }

    private static async Task<IResult> Messages(Guid id, HttpContext context, ClaimsPrincipal user, ClaimsService service,
        CancellationToken cancellationToken, int? page = null, int? pageSize = null, long? throughMessageId = null)
    {
        var window = OptionalPage(page, pageSize);
        if (window is { IsValid: false } invalid)
            return Results.ValidationProblem(invalid.ValidationErrors());
        if (throughMessageId.HasValue && (window is null || throughMessageId <= 0))
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["throughMessageId"] = ["Use a positive message ID with page or pageSize."]
            });

        var messages = await service.MessagesAsync(id, UserId(user), user.IsInRole(Role.ClaimsStaff),
            window, throughMessageId, cancellationToken);
        return PageResult(context, messages);
    }

    private static PageWindow? OptionalPage(int? page, int? pageSize) =>
        page is null && pageSize is null ? null : new PageWindow(page ?? 1, pageSize ?? PageWindow.DefaultSize);

    private static IResult PageResult<T>(HttpContext context, PageItems<T>? page)
    {
        if (page is null)
            return Results.NotFound();

        if (page.Total is { } total)
            context.Response.Headers[PageWindow.TotalHeader] = total.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return Results.Ok(page.Items);
    }

    private static async Task<IResult> GetExposure(Guid id, ClaimsPrincipal user, ClaimsService service,
        CancellationToken cancellationToken)
    {
        var estimate = await service.GetExposureAsync(id, UserId(user), cancellationToken);
        return estimate is null ? Results.NotFound() : Results.Ok(estimate);
    }

    private static async Task<IResult> SetExposure(Guid id, AmountRequest request, ClaimsPrincipal user,
        ClaimsService service, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        var amount = ReadAmount(request.Amount, request.Currency, errors, "amount", "currency", required: true);
        if (amount is null)
            return Results.ValidationProblem(errors);

        var change = await service.SetExposureAsync(id, UserId(user), amount.AmountMinor, amount.Currency, cancellationToken);
        return ChangeResult(change, "An exposure estimate cannot be updated for a closed claim.");
    }

    private static async Task<IResult> Approve(Guid id, AmountRequest? request, ClaimsPrincipal user,
        ClaimsService service, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        var approved = ReadAmount(request?.Amount, request?.Currency, errors, "amount", "currency", required: request is not null);
        if (errors.Count > 0)
            return Results.ValidationProblem(errors);

        var change = await service.ReviewAsync(id, UserId(user), ClaimAction.Approved, body: null, cancellationToken, approved);
        return ChangeResult(change, ReviewConflict);
    }

    private static ClaimAmount? ReadAmount(decimal? value, ExposureCurrency? currency, Dictionary<string, string[]> errors,
        string amountField, string currencyField, bool required = false)
    {
        if (!required && value is null && currency is null)
            return null;

        var errorCount = errors.Count;
        if (value is not { } amount || amount is < 0 or > 1_000_000_000 ||
            decimal.Round(amount, 2) != amount)
        {
            errors[amountField] = ["Enter an amount from 0 to 1,000,000,000 with at most two decimal places."];
        }
        if (currency is null || !Enum.IsDefined(currency.Value))
            errors[currencyField] = ["Select a supported currency."];

        return errors.Count == errorCount ? new ClaimAmount((long)(value!.Value * 100), currency!.Value) : null;
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

    private static void MapRejection(RouteGroupBuilder claims, string method, string route, ClaimAction action, string summary)
    {
        claims.MapMethods(route, [method], async (Guid id, RejectionRequest request, ClaimsPrincipal user,
            ClaimsService service, CancellationToken cancellationToken) =>
        {
            var reason = request.Reason?.Trim();
            if (string.IsNullOrEmpty(reason) || reason.Length > Claim.MaxRejectionReasonLength)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["reason"] = [$"Enter a rejection reason of up to {Claim.MaxRejectionReasonLength} characters."]
                });
            }

            var change = await service.ReviewAsync(id, UserId(user), action, reason, cancellationToken);
            var conflict = action == ClaimAction.RejectionReasonRecorded
                ? "A reason can only be added to a rejected claim that has no recorded reason."
                : ReviewConflict;
            return ChangeResult(change, conflict);
        }).RequireAuthorization(Role.ClaimsStaff).WithSummary(summary).Produces<ClaimView>()
            .ProducesValidationProblem().Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    private const string ReviewConflict = "This action is unavailable for the current claim status.";

    private static IResult ChangeResult(ClaimChange change, string conflictTitle) => change.Outcome switch
    {
        ClaimChangeOutcome.Changed => Results.Ok(change.Claim),
        ClaimChangeOutcome.NotFound => Results.NotFound(),
        _ => Results.Problem(statusCode: StatusCodes.Status409Conflict, title: conflictTitle)
    };

    private static Guid UserId(ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue(JwtRegisteredClaimNames.Sub)!);

    private sealed record SubmitClaim(ClaimType? Type, DateOnly? IncidentDate, string? Description,
        decimal? RequestedAmount, ExposureCurrency? RequestedCurrency);
    private sealed record ReviewMessage(string? Body);
    private sealed record RejectionRequest(string? Reason);
    private sealed record AmountRequest(decimal? Amount, ExposureCurrency? Currency);
}
