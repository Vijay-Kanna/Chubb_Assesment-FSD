using System.IdentityModel.Tokens.Jwt;
using System.Globalization;
using System.Security.Cryptography;
using Claims.Api.Domain;
using Microsoft.IdentityModel.Tokens;
using TokenClaim = System.Security.Claims.Claim;

namespace Claims.Api.Auth;

internal sealed class JwtTokens(IConfiguration configuration, IHostEnvironment environment, TimeProvider clock)
{
    private const string Algorithm = SecurityAlgorithms.HmacSha256;
    private const string RoleClaim = "role";
    private readonly SymmetricSecurityKey key = CreateKey(configuration["Jwt:SigningKey"], environment);
    private readonly string issuer = configuration["Jwt:Issuer"] ?? "chubb-claims";
    private readonly string audience = configuration["Jwt:Audience"] ?? "chubb-claims-api";

    public TokenValidationParameters Validation => new()
    {
        ValidateIssuer = true,
        ValidIssuer = issuer,
        ValidateAudience = true,
        ValidAudience = audience,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = key,
        RequireSignedTokens = true,
        ValidAlgorithms = [Algorithm],
        RequireExpirationTime = true,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromSeconds(15),
        NameClaimType = JwtRegisteredClaimNames.Sub,
        RoleClaimType = RoleClaim
    };

    public LoginResponse Issue(User user)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var expires = now.AddMinutes(20);
        var token = new JwtSecurityToken(
            issuer, audience,
            [
                new TokenClaim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new TokenClaim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new TokenClaim(JwtRegisteredClaimNames.Iat,
                    EpochTime.GetIntDate(now).ToString(CultureInfo.InvariantCulture), System.Security.Claims.ClaimValueTypes.Integer64),
                new TokenClaim(RoleClaim, user.RoleId)
            ],
            notBefore: now,
            expires: expires,
            signingCredentials: new SigningCredentials(key, Algorithm));

        return new LoginResponse(new JwtSecurityTokenHandler().WriteToken(token), expires, user.RoleId);
    }

    private static SymmetricSecurityKey CreateKey(string? configuredKey, IHostEnvironment environment)
    {
        if (string.IsNullOrWhiteSpace(configuredKey))
        {
            if (environment.IsDevelopment())
                return new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32));
            throw new InvalidOperationException("JWT signing key configuration is required.");
        }

        var bytes = Convert.FromBase64String(configuredKey);
        if (bytes.Length < 32)
            throw new InvalidOperationException("JWT signing key must contain at least 32 random bytes.");
        return new SymmetricSecurityKey(bytes);
    }
}

internal sealed record LoginResponse(string AccessToken, DateTime ExpiresAt, string Role);
