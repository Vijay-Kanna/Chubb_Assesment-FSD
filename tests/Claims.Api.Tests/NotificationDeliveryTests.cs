using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Channels;
using Claims.Api.Persistence;
using Claims.Contracts;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Notifications.Api.Persistence;

namespace Claims.Api.Tests;

public sealed class NotificationDeliveryTests
{
    [Fact]
    public async Task Notifications_are_idempotent_and_only_the_recipient_can_read_them()
    {
        var settings = Settings();
        await using var claimsHost = new ClaimsApiFactory { Settings = settings };
        await using var notificationsHost = new NotificationsApiFactory(settings);
        using var claims = CreateClient(claimsHost);
        using var notifications = CreateClient(notificationsHost);
        var claimant = await Login(claims, "claimant");
        var other = await Login(claims, "other-claimant");
        var staff = await Login(claims, "staff");
        var claim = await Submit(claims, claimant);
        var notification = new ClaimNotificationEvent(Guid.NewGuid(), claim.GetProperty("id").GetGuid(),
            claim.GetProperty("claimantId").GetGuid(), ClaimNotificationType.QuestionAsked, DateTime.UtcNow);

        await using (var scope = notificationsHost.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
            Assert.Equal(1, await db.StoreAsync(notification, CancellationToken.None));
            Assert.Equal(0, await db.StoreAsync(notification, CancellationToken.None));
        }

        var visible = Assert.Single(await List(notifications, claimant));
        Assert.Equal(notification.EventId, visible.GetProperty("eventId").GetGuid());
        Assert.Empty(await List(notifications, other));
        using var forbidden = await Send(notifications, HttpMethod.Get, "/notifications", staff);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        using var anonymous = await notifications.GetAsync("/notifications");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    [KafkaFact]
    public async Task Questions_and_decisions_travel_through_Kafka_to_the_correct_claimant()
    {
        var servers = Environment.GetEnvironmentVariable(KafkaFactAttribute.BootstrapVariable)!;
        var topic = $"claim-notifications.test.{Guid.NewGuid():N}";
        var settings = Settings();
        settings["Kafka:BootstrapServers"] = servers;
        settings["Kafka:Topic"] = topic;
        settings["Kafka:GroupId"] = topic;

        using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = servers }).Build();
        await admin.CreateTopicsAsync([new TopicSpecification { Name = topic, NumPartitions = 1, ReplicationFactor = 1 }]);
        try
        {
            await using var claimsHost = new ClaimsApiFactory { Settings = settings };
            await using var notificationsHost = new NotificationsApiFactory(settings);
            using var claims = CreateClient(claimsHost);
            using var notifications = CreateClient(notificationsHost);
            var claimant = await Login(claims, "claimant");
            var staff = await Login(claims, "staff");
            var other = await Login(claims, "other-claimant");
            await using var claimantHub = RealtimeConnection.Create(notificationsHost.Server, "/hubs/notifications", claimant);
            await using var otherHub = RealtimeConnection.Create(notificationsHost.Server, "/hubs/notifications", other);
            var liveNotices = Channel.CreateUnbounded<bool>();
            var otherNotices = Channel.CreateUnbounded<bool>();
            using var claimantListener = claimantHub.On("NotificationsChanged", () => { liveNotices.Writer.TryWrite(true); });
            using var otherListener = otherHub.On("NotificationsChanged", () => { otherNotices.Writer.TryWrite(true); });
            using var liveTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await Task.WhenAll(claimantHub.StartAsync(liveTimeout.Token), otherHub.StartAsync(liveTimeout.Token));
            var claim = await Submit(claims, claimant);
            var id = claim.GetProperty("id").GetGuid();
            var path = $"/claims/{id:D}";

            await Post(claims, $"{path}/pick-up", staff);
            await Post(claims, $"{path}/questions", staff, new { body = "Where was the car parked?" });
            await liveNotices.Reader.ReadAsync(liveTimeout.Token);
            await Post(claims, $"{path}/replies", claimant, new { body = "Outside my home." });
            await Post(claims, $"{path}/approve", staff);

            var delivered = await WaitForType(notifications, claimant, id, "Approved");
            Assert.Equal(2, delivered.Length);
            var question = Assert.Single(delivered, notice => notice.GetProperty("type").GetString() == "QuestionAsked");
            Assert.Equal("More information requested.", question.GetProperty("message").GetString());

            await using (var scope = claimsHost.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ClaimsDbContext>();
                var eventId = question.GetProperty("eventId").GetGuid();
                var outboxEntry = await db.ClaimNotificationOutbox.SingleAsync(entry => entry.EventId == eventId);
                using var producer = new ProducerBuilder<string, string>(new ProducerConfig
                {
                    BootstrapServers = servers,
                    MessageTimeoutMs = 5000
                }).Build();
                await producer.ProduceAsync(topic, new Message<string, string>
                {
                    Key = id.ToString("D"),
                    Value = JsonSerializer.Serialize(outboxEntry.ToEvent(), ClaimNotificationEvent.Json)
                });
            }

            // Waiting for the subsequent same-partition event proves the replay was consumed.
            await Post(claims, $"{path}/settle", staff);
            delivered = await WaitForType(notifications, claimant, id, "Settled");
            Assert.Equal(3, delivered.Length);
            Assert.Single(delivered, notice => notice.GetProperty("type").GetString() == "QuestionAsked");

            var rejectedClaim = await Submit(claims, claimant);
            var rejectedId = rejectedClaim.GetProperty("id").GetGuid();
            await Post(claims, $"/claims/{rejectedId:D}/pick-up", staff);
            const string reason = "The damage occurred before the policy began.";
            await Post(claims, $"/claims/{rejectedId:D}/reject", staff, new { reason });
            var rejected = Assert.Single(await WaitForType(notifications, claimant, rejectedId, "Rejected"));
            Assert.Equal("Your claim was rejected.", rejected.GetProperty("message").GetString());
            Assert.DoesNotContain(reason, rejected.GetRawText(), StringComparison.Ordinal);
            Assert.Empty(await List(notifications, other));
            Assert.False(otherNotices.Reader.TryRead(out _));
        }
        finally
        {
            await admin.DeleteTopicsAsync([topic]);
        }
    }

    private static Dictionary<string, string?> Settings() => new()
    {
        ["Jwt:SigningKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
    };

    private static HttpClient CreateClient<TEntryPoint>(WebApplicationFactory<TEntryPoint> factory) where TEntryPoint : class =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });

    private static async Task<string> Login(HttpClient client, string account)
    {
        using var response = await client.PostAsJsonAsync("/auth/login",
            new { email = $"{account}@example.test", password = ClaimsApiFactory.DemoPassword });
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        return result.GetProperty("accessToken").GetString()!;
    }

    private static Task<JsonElement> Submit(HttpClient client, string token) => Post(client, "/claims", token, new
    {
        type = "Motor",
        incidentDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)),
        description = "Damage to the rear bumper."
    });

    private static async Task<JsonElement> Post(HttpClient client, string path, string token, object? body = null)
    {
        using var response = await Send(client, HttpMethod.Post, path, token, body);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<JsonElement[]> List(HttpClient client, string token)
    {
        using var response = await Send(client, HttpMethod.Get, "/notifications", token);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray().ToArray();
    }

    private static async Task<JsonElement[]> WaitForType(HttpClient client, string token, Guid claimId, string type)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (true)
        {
            var notices = (await List(client, token)).Where(notice => notice.GetProperty("claimId").GetGuid() == claimId).ToArray();
            if (notices.Any(notice => notice.GetProperty("type").GetString() == type))
                return notices;
            await Task.Delay(200, timeout.Token);
        }
    }

    private static async Task<HttpResponseMessage> Send(HttpClient client, HttpMethod method, string path,
        string token, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }

    private sealed class NotificationsApiFactory(Dictionary<string, string?> settings)
        : WebApplicationFactory<Notifications.Api.Program>
    {
        private readonly string database = Path.Combine(Path.GetTempPath(), $"chubb-notifications-{Guid.NewGuid():N}.db");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(settings)
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Notifications"] = $"Data Source={database};Pooling=False"
                }));
        }

        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            File.Delete(database);
        }
    }
}

internal sealed class KafkaFactAttribute : FactAttribute
{
    internal const string BootstrapVariable = "KAFKA_TEST_BOOTSTRAP_SERVERS";

    public KafkaFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(BootstrapVariable)))
            Skip = $"Set {BootstrapVariable} to run the Kafka integration test.";
    }
}
