using Claims.Api.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Claims.Api.Persistence;

internal static class DemoAccounts
{
    public static async Task SeedAsync(IServiceProvider services)
    {
        var password = services.GetRequiredService<IConfiguration>()["Demo:Password"];
        if (string.IsNullOrEmpty(password))
            return;
        if (password.Length is < 12 or > 256)
            throw new InvalidOperationException("Demo:Password must contain 12 to 256 characters.");

        var db = services.GetRequiredService<ClaimsDbContext>();
        var hasher = services.GetRequiredService<IPasswordHasher<User>>();
        var accounts = new User[]
        {
            new() { Name = "Demo Claimant", Email = "claimant@example.test", RoleId = Role.Claimant },
            new() { Name = "Second Claimant", Email = "other-claimant@example.test", RoleId = Role.Claimant },
            new() { Name = "Demo Staff", Email = "staff@example.test", RoleId = Role.ClaimsStaff },
            new() { Name = "Second Staff", Email = "other-staff@example.test", RoleId = Role.ClaimsStaff }
        };

        foreach (var account in accounts)
        {
            var existing = await db.Users.SingleOrDefaultAsync(user => user.Email == account.Email);
            if (existing is null)
            {
                account.PasswordHash = hasher.HashPassword(account, password);
                db.Users.Add(account);
            }
            else if (hasher.VerifyHashedPassword(existing, existing.PasswordHash, password) != PasswordVerificationResult.Success)
            {
                existing.PasswordHash = hasher.HashPassword(existing, password);
            }
        }

        await db.SaveChangesAsync();
    }
}
