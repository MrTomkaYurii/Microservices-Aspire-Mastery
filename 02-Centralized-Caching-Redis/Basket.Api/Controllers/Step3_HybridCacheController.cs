using System.Diagnostics;
using Basket.Api.Models;
using Basket.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Hybrid;

namespace Basket.Api.Controllers;

/// <summary>
/// =========================================================================================
/// 📘 КРОК 3: Сучасне гібридне кешування (.NET 10 HybridCache: L1 + L2)
/// =========================================================================================
/// 
/// 💡 ПРЕАМБУЛА ТА ЖИТТЄВА АНАЛОГІЯ:
/// Чому нам доводилося обирати між супершвидким In-Memory (Крок 1) та надійним спільним Redis (Крок 2)?
/// Нова бібліотека .NET HybridCache об'єднує їх разом у дворівневу систему:
/// 1. Рівень L1 (Локальна пам'ять процесу): Зазираємо сюди першим ділом (~0.01 мс, без мережі).
/// 2. Рівень L2 (Розподілений Redis): Якщо в L1 ще немає — беремо з Redis і одразу зберігаємо в L1.
/// 3. Якщо немає і в Redis — викликається фабрика, що дістає дані з бази даних.
/// 
/// 🛡️ ГОЛОВНА СУПЕРСИЛА: ЗАХИСТ ВІД "ЕФЕКТУ СТАДА БІЗОНІВ" (CACHE STAMPEDE):
/// Уявіть, що під час Чорної п'ятниці у топового товару закінчився термін життя кешу (TTL).
/// В ту саму мілісекунду надходить 2000 одночасних запитів від покупців.
/// Що сталося б у Кроці 2: усі 2000 запитів одночасно пішли б навантажувати базу даних! База падає.
/// Що робить HybridCache: метод GetOrCreateAsync атомарно блокує повторні виклики.
/// До бази даних піде РІВНО ОДИН запит. Решта 1999 запитів зачекають частку мілісекунди
/// і отримають готовий результат із пам'яті. База навіть не відчує навантаження!
/// 
/// 🏷️ ІНВАЛІДАЦІЯ ЗА ТЕГАМИ:
/// Можна об'єднувати ключі в групи тегами: tags: ["baskets"].
/// Виклик RemoveByTagAsync("baskets") скидає всі кошики без небезпечного сканування Redis командою KEYS *.
/// =========================================================================================
/// </summary>
[ApiController]
[Route("api/step3-hybrid")]
[Produces("application/json")]
public class Step3_HybridCacheController : ControllerBase
{
    private readonly HybridCache _hybridCache;
    private readonly BasketStore _store;
    private readonly ILogger<Step3_HybridCacheController> _logger;

    public Step3_HybridCacheController(
        HybridCache hybridCache,
        BasketStore store,
        ILogger<Step3_HybridCacheController> logger)
    {
        _hybridCache = hybridCache;
        _store = store;
        _logger = logger;
    }

    /// <summary>
    /// Отримати кошик користувача через дворівневий HybridCache (L1 RAM -> L2 Redis -> БД).
    /// </summary>
    /// <param name="userName">Ім'я користувача (наприклад: 'john').</param>
    [HttpGet("{userName}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetBasket(string userName, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var cacheKey = $"hybrid:basket:{userName.ToLowerInvariant()}";

        // Метод GetOrCreateAsync виконує всю магію L1 + L2 + Stampede Protection автоматично:
        var cart = await _hybridCache.GetOrCreateAsync(
            cacheKey,
            async cancel =>
            {
                // Цей код виконається ТІЛЬКИ у разі промаху кешу (Cache MISS),
                // причому лише ОДИН РАЗ, навіть якщо 1000 клієнтів звернулися паралельно!
                _logger.LogWarning("🔍 [HybridCache MISS] Завантажуємо кошик для '{User}' з бази даних...", userName);
                return await _store.GetAsync(userName);
            },
            tags: ["baskets", $"user:{userName}"],
            cancellationToken: cancellationToken);

        sw.Stop();

        if (cart is null)
        {
            return NotFound(new { Message = $"Кошик для користувача '{userName}' не знайдено." });
        }

        _logger.LogInformation("⚡ [HybridCache HIT] Кошик для '{User}' успішно повернуто за {Time}ms.",
            userName, sw.Elapsed.TotalMilliseconds);

        return Ok(new
        {
            DataSource = "HybridCache (L1 Memory + L2 Redis)",
            ExecutionTimeMs = sw.Elapsed.TotalMilliseconds,
            Explanation = "Дворівневий кеш: якщо дані вже в L1 — це миттєві мікросекунди; якщо ні — беруться з Redis і кешуються локально.",
            Basket = cart
        });
    }

    /// <summary>
    /// Оновити або зберегти кошик у HybridCache (записується одночасно в L1 пам'ять і в L2 Redis).
    /// </summary>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateBasket([FromBody] ShoppingCart cart, CancellationToken cancellationToken)
    {
        await _store.SaveAsync(cart);

        var cacheKey = $"hybrid:basket:{cart.UserName.ToLowerInvariant()}";

        // Зберігаємо в HybridCache із прив'язкою до тегів для подальшої масової інвалідації
        await _hybridCache.SetAsync(cacheKey, cart, tags: ["baskets", $"user:{cart.UserName}"], cancellationToken: cancellationToken);

        _logger.LogInformation("💾 [HybridCache SET] Кошик для '{User}' оновлено в L1 RAM та L2 Redis.", cart.UserName);

        return Ok(new
        {
            Status = "SavedToDatabaseAndHybridCache",
            Explanation = "Дані збережено в сховище та оновлено на обох рівнях кешу (L1 процесу + L2 Redis) з тегом 'baskets'.",
            cart.UserName,
            cart.TotalPrice
        });
    }

    /// <summary>
    /// Масово інвалідувати всі кошики за тегом 'baskets'.
    /// </summary>
    [HttpPost("clear-by-tag")]
    public async Task<IActionResult> ClearByTag(CancellationToken cancellationToken)
    {
        // Скидає всі записи, помічені тегом "baskets", на обох рівнях кешу
        await _hybridCache.RemoveByTagAsync("baskets", cancellationToken);

        _logger.LogWarning("🧹 [HybridCache TAG INVALIDATION] Усі кошики з тегом 'baskets' успішно інвалідовано!");

        return Ok(new
        {
            Status = "AllBasketsCacheInvalidated",
            Tag = "baskets",
            Explanation = "Завдяки тегам очищено всі кошики без небезпечного сканування ключів Redis (команди KEYS)."
        });
    }
}
