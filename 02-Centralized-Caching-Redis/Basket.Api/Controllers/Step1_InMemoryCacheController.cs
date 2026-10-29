using System.Diagnostics;
using Basket.Api.Models;
using Basket.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace Basket.Api.Controllers;

/// <summary>
/// =========================================================================================
/// 📘 КРОК 1: Вбудоване кешування у пам'яті процесу (ASP.NET Core IMemoryCache)
/// =========================================================================================
/// 
/// 💡 ПРЕАМБУЛА ТА ЖИТТЄВА АНАЛОГІЯ:
/// Уявіть офіціанта в ресторані. Щоб щоразу не бігати на кухню запитувати ціну фірмової страви,
/// він записує її на клаптику паперу і кладе собі в нагрудну кишеню.
/// Коли клієнт запитує ціну, офіціант миттєво дістає папірець із кишені — це займає секунду.
/// 
/// 🔍 ЯК ЦЕ ПРАЦЮЄ В КОДІ:
/// IMemoryCache зберігає об'єкти C# безпосередньо в оперативній пам'яті (RAM) поточного процесу.
/// Тут немає жодних мережевих запитів і навіть серіалізації в JSON. Це найшвидший кеш із усіх
/// можливих (час доступу ~0.001 мілісекунди / мікросекунди).
/// 
/// ⚠️ ГОЛОВНА ПРОБЛЕМА ТА ЧОМУ ЦЬОГО ЗАМАЛО ДЛЯ МІКРОСЕРВІСІВ:
/// Що станеться, коли в ресторані працюватимуть 3 офіціанти (3 репліки нашого мікросервісу)?
/// Клієнт підходить до Офіціанта №1, той записує йому в кошик товар і кладе у СВОЮ кишеню.
/// Наступний запит клієнта балансувальник навантаження відправляє до Офіціанта №2.
/// У кишені Офіціанта №2 пусто! Клієнт бачить порожній кошик.
/// Крім того, якщо процес мікросервісу перезапустити або оновити (deploy), вся RAM стирається.
/// =========================================================================================
/// </summary>
[ApiController]
[Route("api/step1-in-memory")]
[Produces("application/json")]
public class Step1_InMemoryCacheController : ControllerBase
{
    private readonly IMemoryCache _memoryCache;
    private readonly BasketStore _store;
    private readonly ILogger<Step1_InMemoryCacheController> _logger;

    public Step1_InMemoryCacheController(
        IMemoryCache memoryCache,
        BasketStore store,
        ILogger<Step1_InMemoryCacheController> logger)
    {
        _memoryCache = memoryCache;
        _store = store;
        _logger = logger;
    }

    /// <summary>
    /// Отримати кошик користувача з локальної пам'яті процесу (IMemoryCache).
    /// </summary>
    /// <param name="userName">Ім'я користувача (наприклад: 'john').</param>
    [HttpGet("{userName}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetBasket(string userName)
    {
        var sw = Stopwatch.StartNew();
        var cacheKey = $"inmemory:basket:{userName.ToLowerInvariant()}";

        // КРОК 1: Пробуємо дістати кошик із локальної оперативної пам'яті процесу
        if (_memoryCache.TryGetValue(cacheKey, out ShoppingCart? cachedCart))
        {
            sw.Stop();
            _logger.LogInformation("⚡ [IMemoryCache HIT] Кошик для '{User}' знайдено в локальній RAM за {Time}ms.",
                userName, sw.Elapsed.TotalMilliseconds);

            return Ok(new
            {
                DataSource = "In-Memory RAM (Поточний процес)",
                ExecutionTimeMs = sw.Elapsed.TotalMilliseconds,
                Explanation = "Дані повернуто миттєво з локальної пам'яті без звернення до мережі та без серіалізації.",
                Basket = cachedCart
            });
        }

        // КРОК 2: Якщо в пам'яті немає (Cache MISS) — ідемо в постійне сховище (БД)
        _logger.LogWarning("🔍 [IMemoryCache MISS] Кошика для '{User}' немає в пам'яті. Завантажуємо з бази даних...", userName);
        var dbCart = await _store.GetAsync(userName);

        if (dbCart is null)
        {
            return NotFound(new { Message = $"Кошик для користувача '{userName}' не знайдено в базі даних." });
        }

        // КРОК 3: Зберігаємо отримані дані в IMemoryCache з двома правилами закінчення терміну:
        // - AbsoluteExpiration: видалити через 5 хвилин, незалежно від активності.
        // - SlidingExpiration: якщо до кошика не зверталися 1 хвилину — видалити раніше.
        var cacheOptions = new MemoryCacheEntryOptions()
            .SetAbsoluteExpiration(TimeSpan.FromMinutes(5))
            .SetSlidingExpiration(TimeSpan.FromMinutes(1))
            .SetPriority(CacheItemPriority.Normal);

        _memoryCache.Set(cacheKey, dbCart, cacheOptions);

        sw.Stop();
        _logger.LogInformation("💾 [IMemoryCache SAVED] Кошик для '{User}' збережено в RAM на 5 хвилин. Час: {Time}ms.",
            userName, sw.Elapsed.TotalMilliseconds);

        return Ok(new
        {
            DataSource = "Database (Cache MISS -> Saved to RAM)",
            ExecutionTimeMs = sw.Elapsed.TotalMilliseconds,
            Explanation = "Дані зчитано з бази та збережено в локальній пам'яті процесу. Наступний запит буде в рази швидшим.",
            Basket = dbCart
        });
    }

    /// <summary>
    /// Оновити або створити кошик користувача та зберегти його в IMemoryCache.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateBasket([FromBody] ShoppingCart cart)
    {
        await _store.SaveAsync(cart);

        var cacheKey = $"inmemory:basket:{cart.UserName.ToLowerInvariant()}";
        var cacheOptions = new MemoryCacheEntryOptions()
            .SetAbsoluteExpiration(TimeSpan.FromMinutes(5));

        _memoryCache.Set(cacheKey, cart, cacheOptions);

        _logger.LogInformation("✏️ [IMemoryCache UPDATE] Кошик для '{User}' оновлено в базі та локальній пам'яті.", cart.UserName);

        return Ok(new
        {
            Status = "SavedToDatabaseAndMemory",
            Explanation = "Кошик збережено в БД та оновлено в локальній оперативній пам'яті процесу.",
            cart.UserName,
            cart.TotalPrice
        });
    }

    /// <summary>
    /// Очистити локальний кеш кошика в пам'яті.
    /// </summary>
    [HttpDelete("{userName}")]
    public IActionResult InvalidateCache(string userName)
    {
        var cacheKey = $"inmemory:basket:{userName.ToLowerInvariant()}";
        _memoryCache.Remove(cacheKey);

        _logger.LogInformation("🗑️ [IMemoryCache REMOVE] Ключ '{Key}' видалено з оперативної пам'яті.", cacheKey);

        return Ok(new
        {
            Status = "CacheInvalidated",
            Explanation = $"Ключ '{cacheKey}' успішно вилучено з пам'яті процесу."
        });
    }
}
