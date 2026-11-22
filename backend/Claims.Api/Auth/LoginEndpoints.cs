namespace Claims.Api.Auth;

internal static class LoginEndpoints
{
    public static void MapLogin(this WebApplication app)
    {
        app.MapPost("/auth/login", Login)
            .WithTags("Authentication")
            .WithSummary("Sign in and obtain a JWT")
            .AllowAnonymous()
            .RequireRateLimiting("login")
            .Produces<LoginResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status429TooManyRequests);
    }

    private static async Task<IResult> Login(LoginRequest request, LoginService service, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.Email) || request.Email.Length > 254)
            errors["email"] = ["Enter an email address of up to 254 characters."];
        if (string.IsNullOrEmpty(request.Password) || request.Password.Length > 256)
            errors["password"] = ["Enter a password of up to 256 characters."];
        if (errors.Count > 0)
            return Results.ValidationProblem(errors);

        var result = await service.LoginAsync(request.Email!, request.Password!, cancellationToken);
        return result is null ? Results.Unauthorized() : Results.Ok(result);
    }

    private sealed record LoginRequest(string? Email, string? Password);
}
