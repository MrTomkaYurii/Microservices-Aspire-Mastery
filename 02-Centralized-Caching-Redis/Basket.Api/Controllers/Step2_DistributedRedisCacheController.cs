using System.Diagnostics;
using System.Text.Json;
using Basket.Api.Models;
using Basket.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Distributed;

namespace Basket.Api.Controllers;

/// <summary>
/// =========================================================================================
/// 📘 КРОК 2: Чистий розподілений кеш (Redis через IDistributedCache)
/// =========================================================================================
/// 
/// 💡 ПРЕАМБУЛА ТА ЖИТТЄВА АНАЛОГІЯ:
/// Продовжуємо аналогію з рестораном. Оскільки офіціанти не можуть заглядати в кишені один одного,
/// директор ресторану повісив на кухні спільне електронне табло (сервер Redis).
/// Тепер будь-який офіціант може підійти і прочитати з табло однакове замовлення.
/// 
/// 🔍 ЧОМУ РОЗПОДІЛЕНИЙ КЕШ НЕОБХІДНИЙ У МІКРОСЕРВІСАХ:
/// У хмарному середовищі (Kubernetes, Docker) мікросервіс Basket.Api запускається в кількох
/// екземплярах (репліках) для відмовостійкості та високих навантажень.
/// Інтерфейс IDistributedCache звертається до зовнішнього сховища (Redis), спільного для всіх інстансів:
/// 1. Неважливо, на яку саме копію мікросервісу надійде запит користувача — дані скрізь однакові!
/// 2. Якщо контейнер мікросервісу впаде чи оновиться — кеш у Redis не зникне.
/// 
/// ⚖️ КОМПРОМІСИ ТА ЦІНА (ЧОМУ ЦЕ ПОВІЛЬНІШЕ ЗА IN-MEMORY):
/// 1. Мережевий виклик (Network Latency): Доступ до Redis іде через TCP-мережу (1-3 мс замість 0.001 мс у RAM).
/// 2. Серіалізація: Об'єкт C# треба перетворити на JSON-рядок (або байти) при записі,
///    і розпарсити назад у C# об'єкт при кожному читанні. Це навантажує процесор.
/// =========================================================================================
/// </summary>
[ApiController]
[Route("api/step2-distributed-redis")]
[Produces("application/json")]
public class Step2_DistributedRedisCacheController : ControllerBase
{
    private readonly IDistributedCache _distributedCache;
    private readonly BasketStore _store;
    private readonly ILogger<Step2_DistributedRedisCacheController> _logger;

    public Step2_DistributedRedisCacheController(
        IDistributedCache distributedCache,
        BasketStore store,
        ILogger<Step2_DistributedRedisCacheController> logger)
    {
        _distributedCache = distributedCache;
        _store = store;
        _logger = logger;
    }

    /// <summary>
    /// Отримати кошик користувача з розподіленого кешу Redis (IDistributedCache).
    /// </summary>
    /// <param name="userName">Ім'я користувача (наприклад: 'john').</param>
    [HttpGet("{userName}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetBasket(string userName, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var cacheKey = $"distributed:basket:{userName.ToLowerInvariant()}";

        // КРОК 1: Звертаємося до окремого сервера Redis через мережу
        var cachedJson = await _distributedCache.GetStringAsync(cacheKey, cancellationToken);

        if (!string.IsNullOrEmpty(cachedJson))
        {
            // КРОК 2: Десеріалізуємо отриманий рядок JSON в об'єкт C#
            var cachedCart = JsonSerializer.Deserialize<ShoppingCart>(cachedJson);
            sw.Stop();

            _logger.LogInformation("⚡ [Redis Cache HIT] Кошик для '{User}' отримано з Redis за {Time}ms.",
                userName, sw.Elapsed.TotalMilliseconds);

            return Ok(new
            {
                DataSource = "Redis Distributed Cache (Контейнер Redis)",
                ExecutionTimeMs = sw.Elapsed.TotalMilliseconds,
                Explanation = "Дані отримано з центрального Redis. Вони доступні всім реплікам сервісу і збережуться навіть після рестарту застосунку.",
                Basket = cachedCart
            });
        }

        // КРОК 3: Якщо в Redis порожньо (Cache MISS) — звертаємося до бази даних
        _logger.LogWarning("🔍 [Redis Cache MISS] У Redis немає ключа '{Key}'. Читаємо з бази даних...", cacheKey);
        var dbCart = await _store.GetAsync(userName);

        if (dbCart is null)
        {
            return NotFound(new { Message = $"Кошик для '{userName}' не знайдено в базі даних." });
        }

        // КРОК 4: Серіалізуємо об'єкт у JSON і зберігаємо в Redis з часом життя (TTL) 10 хвилин
        var jsonToCache = JsonSerializer.Serialize(dbCart);
        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10),
            SlidingExpiration = TimeSpan.FromMinutes(2)
        };

        await _distributedCache.SetStringAsync(cacheKey, jsonToCache, options, cancellationToken);
        sw.Stop();

        _logger.LogInformation("💾 [Redis Cache SAVED] Кошик для '{User}' записано в Redis на 10 хвилин. Час: {Time}ms.",
            userName, sw.Elapsed.TotalMilliseconds);

        return Ok(new
        {
            DataSource = "Database (Cache MISS -> Saved to Redis)",
            ExecutionTimeMs = sw.Elapsed.TotalMilliseconds,
            Explanation = "Дані зчитано з БД, серіалізовано в JSON і записано в спільний Redis-контейнер.",
            Basket = dbCart
        });
    }

    /// <summary>
    /// Оновити або створити кошик користувача та зберегти його в централізованому Redis.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateBasket([FromBody] ShoppingCart cart, CancellationToken cancellationToken)
    {
        await _store.SaveAsync(cart);

        var cacheKey = $"distributed:basket:{cart.UserName.ToLowerInvariant()}";
        var jsonToCache = JsonSerializer.Serialize(cart);
        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10)
        };

        await _distributedCache.SetStringAsync(cacheKey, jsonToCache, options, cancellationToken);

        _logger.LogInformation("✏️ [Redis Cache UPDATE] Кошик для '{User}' оновлено в базі та Redis.", cart.UserName);

        return Ok(new
        {
            Status = "SavedToDatabaseAndRedis",
            Explanation = "Дані записано в постійне сховище та оновлено в розподіленому Redis. Усі репліки миттєво побачать оновлення.",
            cart.UserName,
            cart.TotalPrice
        });
    }

    /// <summary>
    /// Видалити кошик користувача з централізованого кешу Redis.
    /// </summary>
    [HttpDelete("{userName}")]
    public async Task<IActionResult> InvalidateCache(string userName, CancellationToken cancellationToken)
    {
        var cacheKey = $"distributed:basket:{userName.ToLowerInvariant()}";
        await _distributedCache.RemoveAsync(cacheKey, cancellationToken);

        _logger.LogInformation("🗑️ [Redis Cache REMOVE] Ключ '{Key}' видалено з Redis.", cacheKey);

        return Ok(new
        {
            Status = "RedisCacheInvalidated",
            Key = cacheKey,
            Explanation = "Ключ видалено з сервера Redis. Наступний запит піде в базу даних."
        });
    }
}
