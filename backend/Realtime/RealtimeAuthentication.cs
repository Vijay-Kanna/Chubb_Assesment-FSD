using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Http;

namespace Chubb.Realtime;

internal sealed class SubjectUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection) =>
        Guid.TryParse(connection.User?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var subject)
            ? subject.ToString("D")
            : null;
}

internal static class RealtimeAuthentication
{
    public const string CookieName = "__Host-ClaimsSession";
    public const string RequestHeader = "X-Claims-Workspace";

    public static Task ReadToken(MessageReceivedContext context, PathString hubPath, string? browserOrigin, bool development)
    {
        var request = context.Request;
        if (request.Headers.ContainsKey("Authorization"))
            return Task.CompletedTask;

        if (request.Path == hubPath && request.Query.TryGetValue("access_token", out var token) && token.Count == 1)
        {
            context.Token = token[0];
            return Task.CompletedTask;
        }

        if (!request.Cookies.TryGetValue(CookieName, out var cookie))
            return Task.CompletedTask;

        var isSafe = HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method);
        if (!isSafe && !IsWorkspaceRequest(request))
        {
            context.Fail("Missing workspace request header.");
            return Task.CompletedTask;
        }

        if (request.Path == hubPath && request.Headers.TryGetValue("Origin", out var origin) &&
            !IsAllowedOrigin(request, origin.ToString(), browserOrigin, development))
        {
            context.Fail("Invalid browser origin.");
            return Task.CompletedTask;
        }

        context.Token = cookie;
        return Task.CompletedTask;
    }

    public static bool IsWorkspaceRequest(HttpRequest request) =>
        request.Headers.TryGetValue(RequestHeader, out var value) && value.Count == 1 && value[0] == "1";

    private static bool IsAllowedOrigin(HttpRequest request, string origin, string? browserOrigin, bool development) =>
        string.Equals(origin, browserOrigin ?? $"{request.Scheme}://{request.Host}", StringComparison.OrdinalIgnoreCase) ||
        (development && origin is "http://127.0.0.1:4200" or "http://localhost:4200");
}
