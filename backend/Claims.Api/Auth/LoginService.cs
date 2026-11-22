using Claims.Api.Domain;
using Claims.Api.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Claims.Api.Auth;

internal sealed class LoginService(ClaimsDbContext db, IPasswordHasher<User> hasher, JwtTokens tokens)
{
    public async Task<LoginResponse?> LoginAsync(string email, string password, CancellationToken cancellationToken)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Email == normalizedEmail, cancellationToken);
        if (user is null ||
            hasher.VerifyHashedPassword(user, user.PasswordHash, password) == PasswordVerificationResult.Failed)
        {
            return null;
        }

        return tokens.Issue(user);
    }
}
