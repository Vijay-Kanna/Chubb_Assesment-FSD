using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using System.IdentityModel.Tokens.Jwt;
using Chubb.ApiDocumentation;
using Claims.Api.Application;
using Claims.Api.Auth;
using Claims.Api.Domain;
using Claims.Api.Endpoints;
using Claims.Api.Messaging;
using Claims.Api.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
const string apiTitle = "Claims API";

builder.Services.AddProblemDetails();
builder.Services.AddAssessmentOpenApi(apiTitle);
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false)));
builder.Services.AddDbContext<ClaimsDbContext>((services, options) =>
{
    var config = services.GetRequiredService<IConfiguration>();
    var environment = services.GetRequiredService<IHostEnvironment>();
    var defaultDatabase = Path.Combine(environment.ContentRootPath, "claims.db");
    options.UseSqlite(config.GetConnectionString("Claims") ?? $"Data Source={defaultDatabase}");
});
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<JwtTokens>();
builder.Services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddScoped<LoginService>();
builder.Services.AddScoped<ClaimsService>();
builder.Services.AddHostedService<ClaimNotificationPublisher>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<JwtTokens>((options, tokens) =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = tokens.Validation;
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = context =>
            {
                if (!Guid.TryParse(context.Principal?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out _))
                    context.Fail("Invalid subject.");
                return Task.CompletedTask;
            }
        };
    });
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(Role.Claimant, policy => policy.RequireRole(Role.Claimant))
    .AddPolicy(Role.ClaimsStaff, policy => policy.RequireRole(Role.ClaimsStaff));
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1) }));
});

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
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.UseAssessmentSwagger(apiTitle);
app.MapLogin();
app.MapClaims();

await using (var scope = app.Services.CreateAsyncScope())
{
    _ = scope.ServiceProvider.GetRequiredService<JwtTokens>();
    await scope.ServiceProvider.GetRequiredService<ClaimsDbContext>().Database.MigrateAsync();
    if (app.Environment.IsDevelopment())
        await DemoAccounts.SeedAsync(scope.ServiceProvider);
}

app.Run();

public partial class Program;
