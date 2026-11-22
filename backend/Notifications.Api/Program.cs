using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Chubb.ApiDocumentation;
using Claims.Contracts;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Notifications.Api.Messaging;
using Notifications.Api.Persistence;

namespace Notifications.Api;

public sealed class Program
{
    private const string ApiTitle = "Notifications API";

    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddProblemDetails();
        builder.Services.AddAssessmentOpenApi(ApiTitle);
        builder.Services.AddDbContext<NotificationsDbContext>((services, options) =>
        {
            var config = services.GetRequiredService<IConfiguration>();
            var environment = services.GetRequiredService<IHostEnvironment>();
            var database = Path.Combine(environment.ContentRootPath, "notifications.db");
            options.UseSqlite(config.GetConnectionString("Notifications") ?? $"Data Source={database}");
        });
        builder.Services.AddHostedService<ClaimNotificationConsumer>();

        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IConfiguration>(ConfigureBearer);
        builder.Services.AddAuthorization();

        var app = builder.Build();
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        if (!app.Environment.IsDevelopment())
            app.UseHsts();
        app.UseHttpsRedirection();
        app.Use(async (context, next) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers.XContentTypeOptions = "nosniff";
            await next(context);
        });
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseAssessmentSwagger(ApiTitle);

        app.MapGet("/notifications", ListNotifications)
            .WithTags("Notifications")
            .WithSummary("List notifications for the signed-in claimant")
            .Produces<NotificationView[]>()
            .ProducesValidationProblem()
            .RequireAuthorization(policy => policy.RequireRole("Claimant"));

        _ = app.Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(JwtBearerDefaults.AuthenticationScheme);
        await using (var scope = app.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<NotificationsDbContext>().Database.MigrateAsync();

        await app.RunAsync();
    }

    private static async Task<IResult> ListNotifications(ClaimsPrincipal user, NotificationsDbContext db,
        CancellationToken cancellationToken, int page = 1)
    {
        if (page is < 1 or > 10000)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["page"] = ["Enter a page from 1 to 10000."] });

        var recipient = Guid.Parse(user.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
        var notifications = await db.Notifications.AsNoTracking()
            .Where(notification => notification.RecipientUserId == recipient)
            .OrderByDescending(notification => notification.CreatedAt).ThenBy(notification => notification.EventId)
            .Skip((page - 1) * 50).Take(50).ToListAsync(cancellationToken);

        return Results.Ok(notifications.Select(notification => new NotificationView(
            notification.EventId, notification.ClaimId, notification.Type, notification.CreatedAt,
            notification.Type switch
            {
                ClaimNotificationType.QuestionAsked => "More information requested.",
                ClaimNotificationType.Approved => "Your claim was approved.",
                ClaimNotificationType.Rejected => "Your claim was rejected.",
                ClaimNotificationType.Settled => "Your claim was settled.",
                _ => "Your claim was updated."
            }
        )));
    }

    private static void ConfigureBearer(JwtBearerOptions options, IConfiguration configuration)
    {
        var configuredKey = configuration["Jwt:SigningKey"];
        if (string.IsNullOrWhiteSpace(configuredKey))
            throw new InvalidOperationException("Notifications.Api requires the configured Claims.Api signing key.");

        var bytes = Convert.FromBase64String(configuredKey);
        if (bytes.Length < 32)
            throw new InvalidOperationException("JWT signing key must contain at least 32 random bytes.");

        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = configuration["Jwt:Issuer"] ?? "chubb-claims",
            ValidateAudience = true,
            ValidAudience = configuration["Jwt:Audience"] ?? "chubb-claims-api",
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(bytes),
            RequireSignedTokens = true,
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            RequireExpirationTime = true,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(15),
            NameClaimType = JwtRegisteredClaimNames.Sub,
            RoleClaimType = "role"
        };
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = context =>
            {
                if (!Guid.TryParse(context.Principal?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out _))
                    context.Fail("Invalid subject.");
                return Task.CompletedTask;
            }
        };
    }

    private sealed record NotificationView(Guid EventId, Guid ClaimId, ClaimNotificationType Type,
        DateTime CreatedAt, string Message);
}
