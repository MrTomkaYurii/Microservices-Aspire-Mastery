using System.Text.Json;
using Basket.Api.Models;
using Microsoft.Extensions.Caching.Hybrid;
using StackExchange.Redis;

namespace Basket.Api.Services;

/// <summary>
/// =========================================================================================
/// 📘 ЩО ТАКЕ REDIS PUB/SUB І НАВІЩО ПОТРІБЕН ЦЕЙ ФОНОВИЙ СЛУХАЧ?
/// =========================================================================================
/// 
/// 💡 ПРЕАМБУЛА: ЧОМУ REDIS — ЦЕ НЕ ЛИШЕ СХОВИЩЕ КЛЮЧ-ЗНАЧЕННЯ?
/// Більшість розробників знають Redis як швидку базу даних у пам'яті:
/// ми робимо "SET ключ значення" і "GET ключ".
/// Але в Redis є ще одна надпотужна вбудована функція — **Pub/Sub (Publish / Subscribe)**!
/// 
/// 📻 ЖИТТЄВА АНАЛОГІЯ (РАДІОСТАНЦІЯ):
/// Уявіть радіоприймач.
/// 1. Мікросервіс Pricing.Api — це **радіоведучий (Publisher)**. Коли змінюється ціна на товар,
///    він бере мікрофон і кричить в ефір на хвилі 'pricing-channel': "Увага, товар X тепер коштує 149 грн!".
/// 2. Наш мікросервіс Basket.Api — це **радіослухач (Subscriber)**.
///    Він вмикає приймач на хвилю 'pricing-channel' за допомогою команди:
///    `await subscriber.SubscribeAsync("pricing-channel", (channel, message) => { ... });`
/// 3. Як тільки радіоведучий відправив сигнал, ВСІ приймачі, налаштовані на цю хвилю,
///    миттєво чують це повідомлення і виконують свій код!
/// 
/// ❓ ЧОМУ ЦЕ КРАЩЕ ЗА ІНШІ ВАРІАНТИ В МІКРОСЕРВІСАХ?
/// - Чому не робити періодичне опитування (Polling)?
///   Якби Basket.Api щосекунди звертався до Pricing.Api з питанням "Чи змінилися ціни?",
///   це забило б усю мережу тисячами непотрібних порожніх HTTP-запитів.
/// - Чому не відправити прямий HTTP-запит з Pricing.Api у Basket.Api?
///   Тому що в продакшені Basket.Api запущений у 5 репліках (контейнерах) за балансувальником!
///   Звичайний HTTP-запит натрапить лише на ОДНУ випадкову репліку, а інші 4 залишаться
///   із застарілими неправильними цінами (Stale Cache).
///   А через Redis Pub/Sub сигнал отримують **АБСОЛЮТНО ВСІ РЕПЛІКИ** одночасно!
/// =========================================================================================
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

    /// <summary>
    /// Цей метод автоматично запускається платформою .NET під час старту застосунку
    /// і працює постійно у фоні, очікуючи на повідомлення з Redis.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // КРОК 1: Отримуємо інтерфейс підписника Redis (ISubscriber).
        // IConnectionMultiplexer підтримує одне спільне підключення до Redis для всього застосунку.
        var subscriber = _redis.GetSubscriber();

        _logger.LogInformation("📻 [PricingUpdateSubscriber] Радіоприймач увімкнено! Підписуємося на Redis Pub/Sub канал 'pricing-channel'...");

        // КРОК 2: Викликаємо SubscribeAsync.
        // Ми передаємо ім'я каналу ("pricing-channel") та асинхронний колбек,
        // який Redis буде викликати ЩОРАЗУ, коли хтось відправить повідомлення у цей канал.
        await subscriber.SubscribeAsync(RedisChannel.Literal("pricing-channel"), async (channel, message) =>
        {
            var rawText = message.ToString();
            _logger.LogInformation("📨 [PricingUpdateSubscriber] Отримано радіосигнал із каналу '{Channel}': {Payload}", channel, rawText);

            try
            {
                using var doc = JsonDocument.Parse(rawText);
                var root = doc.RootElement;

                if (root.TryGetProperty("EventType", out var typeProp))
                {
                    var eventType = typeProp.GetString();

                    // СЦЕНАРІЙ А: Зміна ціни на конкретний товар
                    if (eventType == nameof(PriceUpdatedEvent))
                    {
                        var productId = root.GetProperty("ProductId").GetGuid();
                        var newPrice = root.GetProperty("NewPrice").GetDecimal();

                        _logger.LogWarning("⚡ [PricingUpdateSubscriber] Зміна ціни товару {ProductId} на {NewPrice:C}! Виконується інвалідація кешу кошиків...", productId, newPrice);

                        // 1. Оновлюємо базові дані в постійному сховищі
                        _store.UpdateProductPrice(productId, newPrice);

                        // 2. Скидаємо весь кеш кошиків за тегом "baskets" (очищує і L1 RAM, і L2 Redis!)
                        await _cache.RemoveByTagAsync("baskets", stoppingToken);

                        _logger.LogInformation("✅ [PricingUpdateSubscriber] HybridCache очищено за тегом 'baskets'. Наступний запит клієнта отримає вже нову ціну.");
                    }
                    // СЦЕНАРІЙ Б: Застосування глобальної промо-знижки
                    else if (eventType == nameof(DiscountAppliedEvent))
                    {
                        var code = root.GetProperty("DiscountCode").GetString();
                        var percent = root.GetProperty("DiscountPercent").GetDecimal();

                        _logger.LogWarning("🎉 [PricingUpdateSubscriber] Застосовано знижку '{Code}' ({Percent}%)! Очищуємо кеш...", code, percent);

                        _store.ApplyGlobalDiscount(percent);
                        await _cache.RemoveByTagAsync("baskets", stoppingToken);

                        _logger.LogInformation("✅ [PricingUpdateSubscriber] Кеш успішно скинуто. Новий розрахунок врахує промокод.");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ [PricingUpdateSubscriber] Помилка обробки сигналу з Redis Pub/Sub.");
            }
        });

        // КРОК 3: Тримаємо фоновий сервіс активним, поки не буде зупинено сам застосунок
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }
}
