using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nexus.SharedKernel.Context;
using Nexus.SharedKernel.Contracts;
using Nexus.SharedKernel.Events;
using Npgsql;
using RabbitMQ.Client;

namespace Nexus.BuildingBlocks.Messaging;

public interface IEventBus
{
    Task PublishAsync(EventEnvelope envelope, CancellationToken ct = default);
}

public interface IIntegrationEventHandler
{
    string ConsumerName { get; }
    bool CanHandle(string eventType);
    Task HandleAsync(EventEnvelope envelope, CancellationToken ct = default);
}

public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMQ";
    public bool Enabled { get; set; }
    public string HostName { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string UserName { get; set; } = "nexus";
    public string Password { get; set; } = string.Empty;
    public string VirtualHost { get; set; } = "/";
    public string ExchangeName { get; set; } = "nexus.events";
    public string DeadLetterExchangeName { get; set; } = "nexus.events.dlx";
}

public sealed class OutboxOptions
{
    public const string SectionName = "Outbox";
    public bool EnableBackgroundProcessor { get; set; } = true;
    public int BatchSize { get; set; } = 25;
    public int PollIntervalMilliseconds { get; set; } = 2000;
    public int MaxRetries { get; set; } = 5;
}

public sealed class RabbitMqEventBus(
    IOptions<RabbitMqOptions> options,
    ILogger<RabbitMqEventBus> logger) : IEventBus, IAsyncDisposable
{
    private readonly RabbitMqOptions _options = options.Value;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private IConnection? _connection;

    public async Task PublishAsync(EventEnvelope envelope, CancellationToken ct = default)
    {
        var connection = await GetOrCreateConnectionAsync(ct);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: ct);

        await channel.ExchangeDeclareAsync(
            exchange: _options.DeadLetterExchangeName,
            type: ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            cancellationToken: ct);

        await channel.ExchangeDeclareAsync(
            exchange: _options.ExchangeName,
            type: ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            cancellationToken: ct);

        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(envelope, EventJsonOptions.Default));
        var props = new BasicProperties
        {
            MessageId = envelope.EventId.ToString("D"),
            CorrelationId = envelope.CorrelationId,
            Type = envelope.EventType,
            ContentType = "application/json",
            DeliveryMode = DeliveryModes.Persistent,
            Headers = new Dictionary<string, object?>
            {
                ["x-tenant-id"] = envelope.TenantId?.ToString("D") ?? string.Empty,
                ["x-schema-version"] = envelope.SchemaVersion
            }
        };

        await channel.BasicPublishAsync(
            exchange: _options.ExchangeName,
            routingKey: envelope.EventType,
            mandatory: false,
            basicProperties: props,
            body: body,
            cancellationToken: ct);

        logger.LogInformation(
            "Published event {EventType} ({EventId}) to RabbitMQ exchange {Exchange} for tenant {TenantId}",
            envelope.EventType, envelope.EventId, _options.ExchangeName, envelope.TenantId);
    }

    private async Task<IConnection> GetOrCreateConnectionAsync(CancellationToken ct)
    {
        if (_connection is { IsOpen: true })
        {
            return _connection;
        }

        await _lock.WaitAsync(ct);
        try
        {
            if (_connection is { IsOpen: true })
            {
                return _connection;
            }

            var factory = new ConnectionFactory
            {
                HostName = _options.HostName,
                Port = _options.Port,
                UserName = _options.UserName,
                Password = _options.Password,
                VirtualHost = _options.VirtualHost
            };

            _connection = await factory.CreateConnectionAsync(ct);
            return _connection;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }
        _lock.Dispose();
    }
}

public sealed class InMemoryEventBus : IEventBus
{
    private readonly ConcurrentQueue<EventEnvelope> _published = new();
    public Func<EventEnvelope, Exception?>? FailureInjector { get; set; }

    public IReadOnlyCollection<EventEnvelope> PublishedEvents => _published.ToArray();

    public Task PublishAsync(EventEnvelope envelope, CancellationToken ct = default)
    {
        var injected = FailureInjector?.Invoke(envelope);
        if (injected is not null)
        {
            throw injected;
        }

        _published.Enqueue(envelope);
        return Task.CompletedTask;
    }

    public void Clear()
    {
        while (_published.TryDequeue(out _)) { }
    }
}

public sealed class DatabaseOutboxWriter(MessagingDbContext dbContext) : IEventOutboxWriter
{
    public async Task EnqueueAsync(EventEnvelope envelope, CancellationToken ct = default)
    {
        dbContext.OutboxMessages.Add(OutboxMessage.FromEnvelope(envelope));
        await dbContext.SaveChangesAsync(ct);
    }
}

public sealed class IdempotentEventProcessor(
    MessagingDbContext messagingDbContext,
    ITenantContext tenantContext,
    ICorrelationContext correlationContext,
    ISystemClock clock,
    ILogger<IdempotentEventProcessor> logger)
{
    /// <summary>
    /// Processes an incoming event idempotently inside an explicit PostgreSQL transaction
    /// with strict per-message tenant scope isolation.
    /// Returns true if the event was processed, false if it was a duplicate already recorded in Inbox.
    /// </summary>
    public async Task<bool> ProcessAsync(
        EventEnvelope envelope,
        string consumerName,
        Func<EventEnvelope, CancellationToken, Task> handler,
        CancellationToken ct = default)
    {
        correlationContext.SetCorrelationId(envelope.CorrelationId);

        // Enforce strict per-message tenant isolation in background worker context.
        using var scope = envelope.TenantId.HasValue
            ? tenantContext.BeginScope(envelope.TenantId.Value, platformBypass: false, bypassReason: null)
            : tenantContext.BeginScope(null, platformBypass: false, bypassReason: null);

        await using var tx = await messagingDbContext.Database.BeginTransactionAsync(ct);
        try
        {
            var alreadyProcessed = await messagingDbContext.InboxMessages
                .AsNoTracking()
                .AnyAsync(x => x.EventId == envelope.EventId && x.ConsumerName == consumerName, ct);

            if (alreadyProcessed)
            {
                await tx.RollbackAsync(ct);
                return false;
            }

            messagingDbContext.InboxMessages.Add(
                InboxMessage.Create(envelope.EventId, consumerName, envelope.EventType, envelope.TenantId, clock.UtcNow));

            // Save inbox record first to leverage PostgreSQL unique constraint against concurrent duplicate consumers.
            await messagingDbContext.SaveChangesAsync(ct);

            await handler(envelope, ct);

            await tx.CommitAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            await tx.RollbackAsync(ct);
            logger.LogInformation(
                "Duplicate event {EventId} ignored for consumer {ConsumerName} via database unique constraint.",
                envelope.EventId, consumerName);
            return false;
        }
    }
}

public sealed class OutboxDispatcher(
    MessagingDbContext dbContext,
    IEventBus eventBus,
    ISystemClock clock,
    IOptions<OutboxOptions> options,
    ILogger<OutboxDispatcher> logger)
{
    public async Task<int> DispatchBatchAsync(CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var opts = options.Value;
        var processedCount = 0;

        await using var tx = await dbContext.Database.BeginTransactionAsync(ct);

        var messages = await dbContext.OutboxMessages
            .FromSqlInterpolated($@"
                SELECT * FROM messaging.outbox_messages
                WHERE ""Status"" = 'Pending' AND ""NextAttemptAt"" <= {now}
                ORDER BY ""CreatedAt"" ASC
                LIMIT {opts.BatchSize}
                FOR UPDATE SKIP LOCKED")
            .ToListAsync(ct);

        if (messages.Count == 0)
        {
            await tx.CommitAsync(ct);
            return 0;
        }

        foreach (var message in messages)
        {
            try
            {
                var envelope = message.ToEnvelope();
                await eventBus.PublishAsync(envelope, ct);
                message.MarkPublished(clock.UtcNow);
                processedCount++;
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "Failed to publish outbox event {EventId} ({EventType}) on attempt {Attempt}",
                    message.EventId, message.EventType, message.RetryCount + 1);

                message.RecordFailure(ex.Message, clock.UtcNow, opts.MaxRetries);
                if (message.Status == OutboxStatus.DeadLettered)
                {
                    dbContext.DeadLetterMessages.Add(DeadLetterMessage.FromOutbox(message, ex.Message, clock.UtcNow));
                    logger.LogError(
                        "Outbox event {EventId} ({EventType}) moved to dead_letter_messages after {Retries} retries.",
                        message.EventId, message.EventType, message.RetryCount);
                }
            }
        }

        await dbContext.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return processedCount;
    }
}

public sealed class OutboxProcessorBackgroundService(
    IServiceScopeFactory scopeFactory,
    IOptions<OutboxOptions> options,
    ILogger<OutboxProcessorBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.EnableBackgroundProcessor)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var dispatcher = scope.ServiceProvider.GetRequiredService<OutboxDispatcher>();
                await dispatcher.DispatchBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in OutboxProcessorBackgroundService loop.");
            }

            try
            {
                await Task.Delay(options.Value.PollIntervalMilliseconds, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
