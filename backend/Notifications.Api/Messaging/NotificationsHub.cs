using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Notifications.Api.Messaging;

[Authorize(Roles = "Claimant")]
internal sealed class NotificationsHub : Hub
{
    public const string Path = "/hubs/notifications";
}
