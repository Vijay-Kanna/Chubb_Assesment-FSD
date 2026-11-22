using Claims.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Claims.Api.Persistence;

public sealed class ClaimsDbContext(DbContextOptions<ClaimsDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Claim> Claims => Set<Claim>();
    public DbSet<ClaimHistory> ClaimHistory => Set<ClaimHistory>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configuration)
    {
        configuration.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
    }

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Role>().HasData(new Role { Id = Role.Claimant }, new Role { Id = Role.ClaimsStaff });
        model.Entity<User>(user =>
        {
            user.HasIndex(x => x.Email).IsUnique();
            user.HasOne<Role>().WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<Claim>(claim =>
        {
            claim.Property(x => x.Version).IsConcurrencyToken();
            claim.Property(x => x.Type).HasConversion<string>();
            claim.Property(x => x.Status).HasConversion<string>();
            claim.HasOne<User>().WithMany().HasForeignKey(x => x.ClaimantId).OnDelete(DeleteBehavior.Restrict);
            claim.HasOne<User>().WithMany().HasForeignKey(x => x.AssignedStaffId).OnDelete(DeleteBehavior.Restrict);
            claim.HasIndex(x => new { x.AssignedStaffId, x.Status });
        });
        model.Entity<ClaimHistory>(entry =>
        {
            entry.Property(x => x.Action).HasConversion<string>();
            entry.Property(x => x.PreviousStatus).HasConversion<string>();
            entry.Property(x => x.NewStatus).HasConversion<string>();
            entry.HasOne<Claim>().WithMany().HasForeignKey(x => x.ClaimId).OnDelete(DeleteBehavior.Restrict);
            entry.HasOne<User>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        });
    }

    private sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
        value => value, value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
