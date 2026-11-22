using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.SignalR;

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
    public static Task ReadHubToken(MessageReceivedContext context, string hubPath)
    {
        if (context.Request.Path == hubPath &&
            !context.Request.Headers.ContainsKey("Authorization") &&
            context.Request.Query.TryGetValue("access_token", out var token) && token.Count == 1)
        {
            context.Token = token[0];
        }

        return Task.CompletedTask;
    }
}
