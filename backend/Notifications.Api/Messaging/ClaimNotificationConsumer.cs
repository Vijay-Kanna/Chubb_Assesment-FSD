using System.Text.Json;
using Claims.Contracts;
using Confluent.Kafka;
using Microsoft.Data.Sqlite;
using Notifications.Api.Persistence;

namespace Notifications.Api.Messaging;

internal sealed class ClaimNotificationConsumer(IServiceScopeFactory scopes, IConfiguration configuration,
    ILogger<ClaimNotificationConsumer> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var servers = configuration["Kafka:BootstrapServers"];
        if (string.IsNullOrWhiteSpace(servers))
            return;

        var topic = configuration["Kafka:Topic"] ?? ClaimNotificationEvent.DefaultTopic;
        var config = new ConsumerConfig
        {
            BootstrapServers = servers,
            GroupId = configuration["Kafka:GroupId"] ?? "claim-notifications-api-v1",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
            EnableAutoOffsetStore = false
        };

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ConsumeAsync(config, topic, stoppingToken);
            }
            catch (Exception ex) when (ex is KafkaException or SqliteException)
            {
                // Rejoin from the committed offset instead of moving past a failed write.
                logger.LogWarning("Notification consumption will resume after {ErrorType}.", ex.GetType().Name);
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
        }
    }

    private async Task ConsumeAsync(ConsumerConfig config, string topic, CancellationToken cancellationToken)
    {
        using var consumer = new ConsumerBuilder<Ignore, string>(config).Build();
        consumer.Subscribe(topic);
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var record = consumer.Consume(cancellationToken);
                var notification = Parse(record.Message.Value);
                if (notification is null)
                {
                    logger.LogWarning("Ignoring invalid notification event at {Offset}.", record.TopicPartitionOffset);
                }
                else
                {
                    await using var scope = scopes.CreateAsyncScope();
                    var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
                    await db.StoreAsync(notification, cancellationToken);
                }

                consumer.Commit(record);
            }
        }
        finally
        {
            consumer.Close();
        }
    }

    private static ClaimNotificationEvent? Parse(string? value)
    {
        if (value is null || value.Length > 2048)
            return null;

        try
        {
            var notification = JsonSerializer.Deserialize<ClaimNotificationEvent>(value, ClaimNotificationEvent.Json);
            return notification is not null && notification.EventId != Guid.Empty && notification.ClaimId != Guid.Empty &&
                notification.RecipientUserId != Guid.Empty && Enum.IsDefined(notification.Type) &&
                notification.OccurredAt.Kind == DateTimeKind.Utc ? notification : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
