using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using IdentityPlatform.Shared.Database;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace SalesPlattform.Backend.Integrations.Zoho;

/// <summary>
/// Durable CRM job queue. The database stores the job state; RabbitMQ stores
/// the delivery until a worker has processed the job. This keeps queued jobs
/// alive across pod restarts and rebuilds.
/// </summary>
public sealed class ZohoSyncJobStore(
    IOptions<PlatformTenantDatabaseOptions> configuredOptions,
    ILogger<ZohoSyncJobStore> logger) : IDisposable
{
    private const string MessageType = "crm.sync.prepare.sales-plattform.backend";
    private const string QueueName = "identity-platform.crm-sync.sales-plattform.backend";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly PlatformTenantDatabaseOptions options = configuredOptions.Value;
    private readonly object publisherSync = new();
    private readonly ConcurrentDictionary<Guid, byte> scheduledRuns = new();
    private IConnection? publisherConnection;
    private IModel? publisherChannel;

    internal bool IsScheduled(Guid runId) => scheduledRuns.ContainsKey(runId);

    internal Task EnqueueAsync(
        ZohoSyncJobWorkItem workItem,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!scheduledRuns.TryAdd(workItem.RunId, 0))
            return Task.CompletedTask;

        try
        {
            var channel = GetPublisherChannel();
            var properties = channel.CreateBasicProperties();
            properties.Persistent = true;
            properties.ContentType = "application/json";
            properties.Type = MessageType;
            properties.MessageId = workItem.RunId.ToString("D");
            var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
                new ZohoSyncJobEnvelope(MessageType, workItem),
                JsonOptions));
            channel.BasicPublish(options.RabbitMqExchange, MessageType, properties, body);
            channel.WaitForConfirmsOrDie(TimeSpan.FromSeconds(5));
            logger.LogInformation(
                "Queued Zoho import job {RunId} on {QueueName}.",
                workItem.RunId,
                QueueName);
            return Task.CompletedTask;
        }
        catch
        {
            scheduledRuns.TryRemove(workItem.RunId, out _);
            throw;
        }
    }

    internal async Task ConsumeAsync(
        Func<ZohoSyncJobWorkItem, CancellationToken, Task> handler,
        CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ConsumeOnceAsync(handler, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "The Zoho import consumer stopped. Retrying in 5 seconds.");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task ConsumeOnceAsync(
        Func<ZohoSyncJobWorkItem, CancellationToken, Task> handler,
        CancellationToken stoppingToken)
    {
        var factory = new ConnectionFactory
        {
            HostName = options.RabbitMqHost,
            Port = options.RabbitMqPort,
            UserName = options.RabbitMqUsername,
            Password = options.RabbitMqPassword,
            VirtualHost = options.RabbitMqVirtualHost,
            DispatchConsumersAsync = true,
            AutomaticRecoveryEnabled = true,
            NetworkRecoveryInterval = TimeSpan.FromSeconds(10)
        };

        using var connection = factory.CreateConnection("sales-plattform-crm-sync-worker");
        using var channel = connection.CreateModel();
        channel.ExchangeDeclare(options.RabbitMqExchange, ExchangeType.Topic, durable: true, autoDelete: false);
        channel.QueueDeclare(QueueName, durable: true, exclusive: false, autoDelete: false);
        channel.QueueBind(QueueName, options.RabbitMqExchange, MessageType);
        channel.BasicQos(0, 1, false);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.Received += async (_, delivery) =>
        {
            ZohoSyncJobWorkItem? workItem = null;
            var completed = false;
            try
            {
                var envelope = JsonSerializer.Deserialize<ZohoSyncJobEnvelope>(
                    Encoding.UTF8.GetString(delivery.Body.Span),
                    JsonOptions);
                if (envelope?.Payload is null
                    || !string.Equals(envelope.MessageType, MessageType, StringComparison.Ordinal))
                {
                    channel.BasicAck(delivery.DeliveryTag, multiple: false);
                    return;
                }

                workItem = envelope.Payload;
                scheduledRuns.TryAdd(workItem.RunId, 0);
                await handler(workItem, stoppingToken);
                channel.BasicAck(delivery.DeliveryTag, multiple: false);
                completed = true;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                channel.BasicNack(delivery.DeliveryTag, multiple: false, requeue: true);
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Zoho import job message could not be processed and will be retried.");
                channel.BasicNack(delivery.DeliveryTag, multiple: false, requeue: true);
            }
            finally
            {
                if (completed && workItem is not null)
                    scheduledRuns.TryRemove(workItem.RunId, out byte _);
            }
        };

        channel.BasicConsume(QueueName, autoAck: false, consumer);
        logger.LogInformation(
            "Zoho import worker is listening on durable queue {QueueName}.",
            QueueName);
        await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
    }

    private IModel GetPublisherChannel()
    {
        lock (publisherSync)
        {
            if (publisherChannel is { IsOpen: true })
                return publisherChannel;

            DisposePublisherConnection();
            var factory = new ConnectionFactory
            {
                HostName = options.RabbitMqHost,
                Port = options.RabbitMqPort,
                UserName = options.RabbitMqUsername,
                Password = options.RabbitMqPassword,
                VirtualHost = options.RabbitMqVirtualHost,
                AutomaticRecoveryEnabled = true,
                NetworkRecoveryInterval = TimeSpan.FromSeconds(10)
            };
            publisherConnection = factory.CreateConnection("sales-plattform-crm-sync-publisher");
            publisherChannel = publisherConnection.CreateModel();
            publisherChannel.ExchangeDeclare(options.RabbitMqExchange, ExchangeType.Topic, durable: true, autoDelete: false);
            publisherChannel.QueueDeclare(QueueName, durable: true, exclusive: false, autoDelete: false);
            publisherChannel.QueueBind(QueueName, options.RabbitMqExchange, MessageType);
            publisherChannel.ConfirmSelect();
            return publisherChannel;
        }
    }

    public void Dispose()
    {
        lock (publisherSync)
        {
            DisposePublisherConnection();
        }
    }

    private void DisposePublisherConnection()
    {
        try { publisherChannel?.Dispose(); } catch { /* best effort during shutdown */ }
        try { publisherConnection?.Dispose(); } catch { /* best effort during shutdown */ }
        publisherChannel = null;
        publisherConnection = null;
    }

    private sealed record ZohoSyncJobEnvelope(
        string MessageType,
        ZohoSyncJobWorkItem? Payload);
}
