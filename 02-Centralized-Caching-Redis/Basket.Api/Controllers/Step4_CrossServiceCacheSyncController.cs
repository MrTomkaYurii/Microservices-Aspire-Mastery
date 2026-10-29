using Basket.Api.Models;
using Basket.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Hybrid;

namespace Basket.Api.Controllers;

/// <summary>
/// =========================================================================================
/// 📘 КРОК 4: Синхронізація кешу між різними мікросервісами (Redis Pub/Sub Invalidation)
/// =========================================================================================
/// 
/// 💡 ПРЕАМБУЛА ТА ЖИТТЄВА АНАЛОГІЯ:
/// Уявіть, що ціни встановлює бухгалтерія на другому поверсі (сервіс Pricing.Api).
/// А офіціанти обслуговують столики на першому поверсі (сервіс Basket.Api).
/// Якщо бухгалтер знижує ціну на піцу з 200 до 150 грн, офіціант на першому поверсі
/// ніяк не може про це дізнатися зі свого блокнота (кешу)!
/// Щоб офіціант не продав страву за старою ціною, бухгалтер використовує внутрішнє радіо (Redis Pub/Sub)
/// і оголошує на весь ресторан: "Ціну на піцу змінено!".
/// Офіціант чує це по радіо, викреслює стару ціну зі свого блокнота і заглядає за новою.
/// 
/// 🔍 ЯК ЦЕ ПРАЦЮЄ В АРХІТЕКТУРІ МІКРОСЕРВІСІВ:
/// 1. Мікросервіс Pricing.Api змінює ціну і викликає:
///    sub.PublishAsync("pricing-channel", payload);
/// 2. У нашому Basket.Api працює фоновий фоновий слухач (PricingUpdateSubscriber):
///    Він постійно слухає цей канал через Redis ISubscriber.
/// 3. Як тільки надходить сигнал, фоновий воркер викликає:
///    await _hybridCache.RemoveByTagAsync("baskets");
/// 4. Кеш миттєво очищується. Коли клієнт запитує кошик наступного разу,
///    він отримує актуальну перераховану суму з урахуванням нової ціни чи знижки!
/// =========================================================================================
/// </summary>
[ApiController]
[Route("api/step4-cross-service-sync")]
[Produces("application/json")]
public class Step4_CrossServiceCacheSyncController : ControllerBase
{
    private readonly HybridCache _hybridCache;
    private readonly BasketStore _store;
    private readonly ILogger<Step4_CrossServiceCacheSyncController> _logger;

    public Step4_CrossServiceCacheSyncController(
        HybridCache hybridCache,
        BasketStore store,
        ILogger<Step4_CrossServiceCacheSyncController> logger)
    {
        _hybridCache = hybridCache;
        _store = store;
        _logger = logger;
    }

    /// <summary>
    /// Отримати стан кошика користувача.
    /// Перевірте цей ендпоінт ДО і ПІСЛЯ того, як зміните ціну в Pricing.Api!
    /// </summary>
    /// <param name="userName">Ім'я користувача (наприклад: 'john').</param>
    [HttpGet("basket/{userName}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetBasket(string userName, CancellationToken cancellationToken)
    {
        var cacheKey = $"sync:basket:{userName.ToLowerInvariant()}";

        var cart = await _hybridCache.GetOrCreateAsync(
            cacheKey,
            async cancel =>
            {
                _logger.LogWarning("🔍 [Step4 MISS] Кошик для '{User}' зчитується зі сховища...", userName);
                return await _store.GetAsync(userName);
            },
            tags: ["baskets", $"user:{userName}"],
            cancellationToken: cancellationToken);

        if (cart is null)
        {
            return NotFound(new { Message = $"Кошик для '{userName}' не знайдено." });
        }

        return Ok(new
        {
            Explanation = "Сума TotalPrice автоматично перераховується, якщо Pricing.Api надіслав подію через Redis Pub/Sub.",
            cart.UserName,
            cart.DiscountPercent,
            cart.TotalPrice,
            Items = cart.Items.Select(i => new
            {
                i.ProductId,
                i.ProductName,
                i.UnitPrice,
                i.Quantity,
                i.TotalItemPrice
            })
        });
    }

    /// <summary>
    /// Пояснення життєвого циклу інвалідації через Redis Pub/Sub.
    /// </summary>
    [HttpGet("how-it-works")]
    public IActionResult ExplainSyncFlow()
    {
        return Ok(new
        {
            Architecture = "Event-Driven Cache Invalidation over Redis Pub/Sub",
            Steps = new[]
            {
                "1. Pricing.Api публікує подію у канал 'pricing-channel' через ISubscriber.PublishAsync().",
                "2. Фоновий сервіс PricingUpdateSubscriber у Basket.Api отримує повідомлення у фоні.",
                "3. Воркер оновлює дані в BasketStore та викликає _cache.RemoveByTagAsync('baskets').",
                "4. При наступному запиті покупця кошик зчитує вже нову ціну без перезавантаження сервісу."
            }
        });
    }
}
