using System.Text.Json;
using Claims.Api.Persistence;
using Claims.Contracts;
using Confluent.Kafka;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Claims.Api.Messaging;

internal sealed class ClaimNotificationPublisher(IServiceScopeFactory scopes, IConfiguration configuration,
    TimeProvider clock, ILogger<ClaimNotificationPublisher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var servers = configuration["Kafka:BootstrapServers"];
        if (string.IsNullOrWhiteSpace(servers))
            return;

        var topic = configuration["Kafka:Topic"] ?? ClaimNotificationEvent.DefaultTopic;
        using var producer = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = servers,
            EnableIdempotence = true,
            MessageTimeoutMs = 5000
        }).Build();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var published = await PublishBatchAsync(producer, topic, stoppingToken);
                if (published == 0)
                    await Task.Delay(TimeSpan.FromMilliseconds(500), stoppingToken);
            }
            catch (Exception ex) when (ex is KafkaException or SqliteException or DbUpdateException)
            {
                logger.LogWarning("Notification publishing paused after {ErrorType}. Pending events will be retried.",
                    ex.GetType().Name);
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
        }
    }

    private async Task<int> PublishBatchAsync(IProducer<string, string> producer, string topic, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ClaimsDbContext>();
        var pending = await db.ClaimNotificationOutbox
            .Where(entry => entry.PublishedAt == null)
            .OrderBy(entry => entry.Id)
            .Take(25)
            .ToListAsync(cancellationToken);

        foreach (var entry in pending)
        {
            await producer.ProduceAsync(topic, new Message<string, string>
            {
                Key = entry.ClaimId.ToString("D"),
                Value = JsonSerializer.Serialize(entry.ToEvent(), ClaimNotificationEvent.Json)
            }, cancellationToken);

            entry.MarkPublished(clock.GetUtcNow().UtcDateTime);
            await db.SaveChangesAsync(cancellationToken);
        }

        return pending.Count;
    }
}
