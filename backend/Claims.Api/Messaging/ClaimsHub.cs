using Claims.Api.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Claims.Api.Messaging;

[Authorize(Roles = Role.Claimant + "," + Role.ClaimsStaff)]
internal sealed class ClaimsHub : Hub
{
    public const string Path = "/hubs/claims";
    internal const string StaffGroup = "claims-staff";

    public override async Task OnConnectedAsync()
    {
        if (Context.User?.IsInRole(Role.ClaimsStaff) == true)
            await Groups.AddToGroupAsync(Context.ConnectionId, StaffGroup, Context.ConnectionAborted);

        await base.OnConnectedAsync();
    }
}

internal sealed class ClaimUpdates(IHubContext<ClaimsHub> hub, ILogger<ClaimUpdates> logger)
{
    public async Task PublishAsync(Guid claimantId)
    {
        // Keep claim data on authorized HTTP endpoints. Push only that something changed.
        try
        {
            await Task.WhenAll(
                hub.Clients.User(claimantId.ToString("D")).SendAsync("ClaimsChanged"),
                hub.Clients.Group(ClaimsHub.StaffGroup).SendAsync("ClaimsChanged"));
        }
        catch (Exception exception)
        {
            logger.LogWarning("Could not send a live claim update ({ErrorType}).", exception.GetType().Name);
        }
    }
}
