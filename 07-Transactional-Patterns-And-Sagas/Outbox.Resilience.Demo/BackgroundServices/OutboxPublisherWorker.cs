using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Outbox.Resilience.Demo.Data;

namespace Outbox.Resilience.Demo.BackgroundServices;

public class OutboxPublisherWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<OutboxPublisherWorker> _logger;
    private readonly ResiliencePipeline _resiliencePipeline;

    public OutboxPublisherWorker(IServiceProvider serviceProvider, ILogger<OutboxPublisherWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;

        // Configure modern Polly v8 Resilience Pipeline (Retry + Circuit Breaker)
        _resiliencePipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromMilliseconds(200),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                OnRetry = args =>
                {
                    _logger.LogWarning("Retrying message publishing. Attempt: {Attempt}, Delay: {Delay}", args.AttemptNumber, args.RetryDelay);
                    return ValueTask.CompletedTask;
                }
            })
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                FailureRatio = 0.5,
                SamplingDuration = TimeSpan.FromSeconds(10),
                MinimumThroughput = 5,
                BreakDuration = TimeSpan.FromSeconds(15),
                OnOpened = _ =>
                {
                    _logger.LogError("Polly Circuit Breaker OPENED: Message broker is unreachable or failing.");
                    return ValueTask.CompletedTask;
                },
                OnClosed = _ =>
                {
                    _logger.LogInformation("Polly Circuit Breaker CLOSED: Message broker restored.");
                    return ValueTask.CompletedTask;
                }
            })
            .Build();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Outbox Publisher Worker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                var pendingMessages = await dbContext.OutboxMessages
                    .Where(m => m.ProcessedAt == null)
                    .OrderBy(m => m.CreatedAt)
                    .Take(20)
                    .ToListAsync(stoppingToken);

                foreach (var message in pendingMessages)
                {
                    try
                    {
                        await _resiliencePipeline.ExecuteAsync(async ct =>
                        {
                            await SimulateBrokerPublishAsync(message.EventType, message.Payload, ct);
                        }, stoppingToken);

                        message.ProcessedAt = DateTime.UtcNow;
                        _logger.LogInformation("Outbox Message {MessageId} published successfully.", message.Id);
                    }
                    catch (Exception ex)
                    {
                        message.RetryCount++;
                        message.ErrorMessage = ex.Message;
                        _logger.LogError(ex, "Failed to publish Outbox Message {MessageId}. Retry count: {Count}", message.Id, message.RetryCount);
                    }
                }

                if (pendingMessages.Count > 0)
                {
                    await dbContext.SaveChangesAsync(stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error during outbox processing cycle.");
            }

            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        }
    }

    private static async Task SimulateBrokerPublishAsync(string eventType, string payload, CancellationToken ct)
    {
        await Task.Delay(50, ct); // Simulate RabbitMQ/Kafka network I/O
    }
}
