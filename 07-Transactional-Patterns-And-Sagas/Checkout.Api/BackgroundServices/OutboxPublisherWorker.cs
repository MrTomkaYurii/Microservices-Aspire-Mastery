using Checkout.Api.Data;
using Microsoft.EntityFrameworkCore;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;

namespace Checkout.Api.BackgroundServices;

/// <summary>
/// 🔄 ФОНОВИЙ ВОРКЕР ПАТЕРНУ TRANSACTIONAL OUTBOX З НАДІЙНІСТЮ POLLY v8
/// 
/// 💡 НАВІЩО ПОТРІБЕН POLLY v8 ТУТ?
/// Коли брокер RabbitMQ перезавантажується або мережа переривається:
/// 1. Retry Strategy (з Exponential Backoff та Jitter):
///    - Замість бомбардування брокера щомілісекунди, інтервал збільшується: 200ms -> 400ms -> 800ms.
///    - Jitter додає випадкову дельту, щоб 100 воркерів не вдарили в брокер в одну й ту ж секунду.
/// 2. Circuit Breaker (Автоматичний вимикач):
///    - Якщо 50% спроб відправки падають, Circuit Breaker переходить у стан OPEN.
///    - Запити до брокера тимчасово припиняються на 15 секунд, даючи RabbitMQ час відновитися.
/// </summary>
public class OutboxPublisherWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<OutboxPublisherWorker> _logger;
    private readonly ResiliencePipeline _resiliencePipeline;

    // Прапорець для симуляції збою брокера зі Swagger
    public static bool SimulateBrokerOutage = false;

    public OutboxPublisherWorker(IServiceProvider serviceProvider, ILogger<OutboxPublisherWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;

        _resiliencePipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromMilliseconds(200),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                OnRetry = args =>
                {
                    _logger.LogWarning("⚠️ [Polly Retry] Спроба {Attempt}: Помилка публікації Outbox. Очікування {Delay} перед повтором...", 
                        args.AttemptNumber, args.RetryDelay);
                    return ValueTask.CompletedTask;
                }
            })
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                FailureRatio = 0.5,
                SamplingDuration = TimeSpan.FromSeconds(10),
                MinimumThroughput = 4,
                BreakDuration = TimeSpan.FromSeconds(15),
                OnOpened = _ =>
                {
                    _logger.LogError("🛑 [Polly Circuit Breaker] СТАН: OPEN! Брокер повідомлень недоступний. Публікація призупинена на 15с.");
                    return ValueTask.CompletedTask;
                },
                OnClosed = _ =>
                {
                    _logger.LogInformation("✅ [Polly Circuit Breaker] СТАН: CLOSED! Зв'язок із брокером відновлено.");
                    return ValueTask.CompletedTask;
                }
            })
            .Build();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("🚀 Outbox Publisher Worker запущено.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                var pendingMessages = await dbContext.OutboxMessages
                    .Where(m => m.ProcessedAt == null)
                    .OrderBy(m => m.CreatedAt)
                    .Take(10)
                    .ToListAsync(stoppingToken);

                foreach (var message in pendingMessages)
                {
                    try
                    {
                        await _resiliencePipeline.ExecuteAsync(async ct =>
                        {
                            if (SimulateBrokerOutage)
                            {
                                throw new HttpRequestException("RabbitMQ connection dropped (Simulated Outage)!");
                            }

                            // Симуляція відправки в RabbitMQ
                            await Task.Delay(25, ct);
                        }, stoppingToken);

                        message.ProcessedAt = DateTime.UtcNow;
                        _logger.LogInformation("📤 Outbox повідомлення {MessageId} ({EventType}) успішно надіслано в брокер.", 
                            message.Id, message.EventType);
                    }
                    catch (Exception ex)
                    {
                        message.RetryCount++;
                        message.ErrorMessage = ex.Message;
                        _logger.LogError(ex, "❌ Помилка обробки Outbox повідомлення {MessageId}. Кількість спроб: {Count}", 
                            message.Id, message.RetryCount);
                    }
                }

                if (pendingMessages.Count > 0)
                {
                    await dbContext.SaveChangesAsync(stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Помилка ітерації циклу обробки Outbox.");
            }

            await Task.Delay(2000, stoppingToken);
        }
    }
}
