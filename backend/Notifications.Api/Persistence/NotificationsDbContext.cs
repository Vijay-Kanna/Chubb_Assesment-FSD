using Claims.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Notifications.Api.Persistence;

public sealed class NotificationsDbContext(DbContextOptions<NotificationsDbContext> options) : DbContext(options)
{
    public DbSet<Notification> Notifications => Set<Notification>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Notification>(notification =>
        {
            notification.HasKey(x => x.EventId);
            notification.Property(x => x.EventId).ValueGeneratedNever();
            notification.Property(x => x.Type).HasConversion<string>();
            notification.Property(x => x.CreatedAt).HasConversion(value => value,
                value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
            notification.HasIndex(x => new { x.RecipientUserId, x.CreatedAt });
        });
    }

    // SQLite makes duplicate detection atomic with the insert. Interpolation binds all values as parameters.
    public Task<int> StoreAsync(ClaimNotificationEvent notification, CancellationToken cancellationToken) =>
        Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "Notifications" ("EventId", "ClaimId", "RecipientUserId", "Type", "CreatedAt")
            VALUES ({notification.EventId}, {notification.ClaimId}, {notification.RecipientUserId},
                    {notification.Type.ToString()}, {notification.OccurredAt})
            ON CONFLICT ("EventId") DO NOTHING;
            """, cancellationToken);
}

public sealed class Notification
{
    public Guid EventId { get; init; }
    public Guid ClaimId { get; init; }
    public Guid RecipientUserId { get; init; }
    public ClaimNotificationType Type { get; init; }
    public DateTime CreatedAt { get; init; }
}
