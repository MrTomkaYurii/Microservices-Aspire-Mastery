using System.Text.Json;
using Basket.Api.Models;
using Microsoft.Extensions.Caching.Hybrid;
using StackExchange.Redis;

namespace Basket.Api.Services;

/// <summary>
/// Фоновий воркер, що підписується на Redis Pub/Sub канал 'pricing-channel'.
/// При отриманні події зміни ціни або знижки від Pricing.Api,
/// автоматично інвалідує HybridCache і провокує перерахунок счёта кошика.
/// </summary>
public class PricingUpdateSubscriber : BackgroundService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly BasketStore _store;
    private readonly HybridCache _cache;
    private readonly ILogger<PricingUpdateSubscriber> _logger;

    public PricingUpdateSubscriber(
        IConnectionMultiplexer redis,
        BasketStore store,
        HybridCache cache,
        ILogger<PricingUpdateSubscriber> logger)
    {
        _redis = redis;
        _store = store;
        _cache = cache;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var subscriber = _redis.GetSubscriber();

        _logger.LogInformation("📡 [PricingUpdateSubscriber] Підписка на Redis Pub/Sub канал 'pricing-channel' оформлена.");

        await subscriber.SubscribeAsync(RedisChannel.Literal("pricing-channel"), async (channel, message) =>
        {
            var rawText = message.ToString();
            _logger.LogInformation("📨 [PricingUpdateSubscriber] Отримано сигнал із каналу '{Channel}': {Payload}", channel, rawText);

            try
            {
                using var doc = JsonDocument.Parse(rawText);
                var root = doc.RootElement;

                if (root.TryGetProperty("EventType", out var typeProp))
                {
                    var eventType = typeProp.GetString();

                    if (eventType == nameof(PriceUpdatedEvent))
                    {
                        var productId = root.GetProperty("ProductId").GetGuid();
                        var newPrice = root.GetProperty("NewPrice").GetDecimal();

                        _logger.LogWarning("⚡ [PricingUpdateSubscriber] Зміна ціни товару {ProductId} на {NewPrice:C}! Виконується інвалідація кешу кошиків...", productId, newPrice);

                        _store.UpdateProductPrice(productId, newPrice);

                        // Інвалідуємо кеш кошиків за тегом "baskets"
                        await _cache.RemoveByTagAsync("baskets", stoppingToken);

                        _logger.LogInformation("✅ [PricingUpdateSubscriber] HybridCache очищено за тегом 'baskets'. Наступний запит отримає оновлений підсумок.");
                    }
                    else if (eventType == nameof(DiscountAppliedEvent))
                    {
                        var code = root.GetProperty("DiscountCode").GetString();
                        var percent = root.GetProperty("DiscountPercent").GetDecimal();

                        _logger.LogWarning("🎉 [PricingUpdateSubscriber] Застосовано знижку '{Code}' ({Percent}%)! Виконується інвалідація кешу...", code, percent);

                        _store.ApplyGlobalDiscount(percent);
                        await _cache.RemoveByTagAsync("baskets", stoppingToken);

                        _logger.LogInformation("✅ [PricingUpdateSubscriber] HybridCache очищено. Новий підсумок кошика врахує знижку.");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ [PricingUpdateSubscriber] Помилка обробки події інвалідації з Redis Pub/Sub.");
            }
        });

        // Чекаємо завершення сервісу
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }
}
