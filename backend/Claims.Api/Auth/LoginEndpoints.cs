using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Chubb.Realtime;
using Claims.Api.Domain;
using Claims.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Claims.Api.Auth;

internal static class LoginEndpoints
{
    public static void MapLogin(this WebApplication app)
    {
        app.MapPost("/auth/login", (LoginRequest request, LoginService service, HttpContext context, CancellationToken cancellationToken) =>
                Login(request, service, context, browser: false, cancellationToken))
            .WithTags("Authentication")
            .WithSummary("Sign in and obtain a JWT")
            .AllowAnonymous()
            .RequireRateLimiting("login")
            .Produces<LoginResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status429TooManyRequests);

        var browser = app.MapGroup("/auth/session").ExcludeFromDescription();
        browser.MapPost("", (LoginRequest request, LoginService service, HttpContext context, CancellationToken cancellationToken) =>
                Login(request, service, context, browser: true, cancellationToken))
            .AllowAnonymous().RequireRateLimiting("login");
        browser.MapGet("", Restore).RequireAuthorization(policy => policy.RequireRole(Role.Claimant, Role.ClaimsStaff));
        browser.MapDelete("", SignOut).AllowAnonymous();
    }

    private static async Task<IResult> Login(LoginRequest request, LoginService service, HttpContext context,
        bool browser, CancellationToken cancellationToken)
    {
        if (browser && !RealtimeAuthentication.IsWorkspaceRequest(context.Request))
            return Results.StatusCode(StatusCodes.Status403Forbidden);

        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.Email) || request.Email.Length > 254)
            errors["email"] = ["Enter an email address of up to 254 characters."];
        if (string.IsNullOrEmpty(request.Password) || request.Password.Length > 256)
            errors["password"] = ["Enter a password of up to 256 characters."];
        if (errors.Count > 0)
            return Results.ValidationProblem(errors);

        var result = await service.LoginAsync(request.Email!, request.Password!, cancellationToken);
        if (result is not { } signIn)
            return Results.Unauthorized();
        if (!browser)
            return Results.Ok(signIn.Token);

        var (user, token) = signIn;
        var cookie = SessionCookie();
        cookie.Expires = token.ExpiresAt;
        context.Response.Cookies.Append(RealtimeAuthentication.CookieName, token.AccessToken, cookie);
        return Results.Ok(new BrowserSession(user.Id, user.Email, token.Role, token.ExpiresAt));
    }

    private static async Task<IResult> Restore(ClaimsPrincipal principal, ClaimsDbContext db,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub), out var userId) ||
            !long.TryParse(principal.FindFirstValue(JwtRegisteredClaimNames.Exp), NumberStyles.None,
                CultureInfo.InvariantCulture, out var expiry))
            return Results.Unauthorized();

        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(user => user.Id == userId, cancellationToken);
        if (user is null || !principal.IsInRole(user.RoleId))
            return Results.Unauthorized();

        return Results.Ok(new BrowserSession(user.Id, user.Email, user.RoleId, DateTimeOffset.FromUnixTimeSeconds(expiry).UtcDateTime));
    }

    private static IResult SignOut(HttpContext context)
    {
        if (!RealtimeAuthentication.IsWorkspaceRequest(context.Request))
            return Results.StatusCode(StatusCodes.Status403Forbidden);

        context.Response.Cookies.Delete(RealtimeAuthentication.CookieName, SessionCookie());
        return Results.NoContent();
    }

    private static CookieOptions SessionCookie() =>
        new() { HttpOnly = true, Secure = true, SameSite = SameSiteMode.Strict, Path = "/" };

    private sealed record LoginRequest(string? Email, string? Password);
    private sealed record BrowserSession(Guid UserId, string Email, string Role, DateTime ExpiresAt);
}
