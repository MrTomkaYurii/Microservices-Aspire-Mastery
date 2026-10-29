using Basket.Api.Models;
using Basket.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Hybrid;

namespace Basket.Api.Controllers;

/// <summary>
/// Контролер управління кошиком користувача.
/// Демонструє багаторівневе кешування .NET 10 HybridCache (L1 Memory + L2 Redis),
/// захист від Cache Stampede та інвалідацію кешу за тегами.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class BasketController : ControllerBase
{
    private readonly HybridCache _cache;
    private readonly BasketStore _store;
    private readonly ILogger<BasketController> _logger;

    public BasketController(HybridCache cache, BasketStore store, ILogger<BasketController> logger)
    {
        _cache = cache;
        _store = store;
        _logger = logger;
    }

    /// <summary>
    /// Отримати кошик користувача.
    /// Використовує HybridCache (L1 In-Memory + L2 Redis) з захистом від Cache Stampede.
    /// </summary>
    /// <param name="userName">Ім'я або логін користувача (наприклад: 'john').</param>
    /// <param name="cancellationToken">Токен скасування.</param>
    /// <returns>Дані кошика з актуальною ціною.</returns>
    [HttpGet("{userName}")]
    [ProducesResponseType(typeof(ShoppingCart), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ShoppingCart>> GetBasket(string userName, CancellationToken cancellationToken)
    {
        var cacheKey = $"basket:{userName.ToLowerInvariant()}";

        // GetOrCreateAsync захищає від Cache Stampede:
        // якщо 100 клієнтів одночасно звернуться за кошиком, фабрика виконається лише 1 раз!
        var cart = await _cache.GetOrCreateAsync(
            cacheKey,
            async cancel =>
            {
                _logger.LogWarning("🔍 [Cache MISS] Кошик для '{User}' не знайдено в HybridCache. Завантажуємо зі сховища...", userName);
                return await _store.GetAsync(userName);
            },
            tags: ["baskets", $"user:{userName}"],
            cancellationToken: cancellationToken);

        if (cart is null)
        {
            _logger.LogInformation("Кошик для користувача '{User}' порожній або відсутній.", userName);
            return NotFound(new { Message = $"Кошик для користувача '{userName}' порожній або не існує." });
        }

        _logger.LogInformation("⚡ [Cache HIT] Кошик для '{User}' повернуто з HybridCache. Разом до сплати: {Total:C}", userName, cart.TotalPrice);
        return Ok(cart);
    }

    /// <summary>
    /// Створити або оновити кошик користувача, зберігши його в сховищі та записавши в HybridCache.
    /// </summary>
    /// <param name="cart">Об'єкт кошика з товарами.</param>
    /// <param name="cancellationToken">Токен скасування.</param>
    /// <returns>Оновлений кошик.</returns>
    [HttpPost]
    [ProducesResponseType(typeof(ShoppingCart), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ShoppingCart>> UpdateBasket([FromBody] ShoppingCart cart, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(cart.UserName))
        {
            return BadRequest(new { Message = "UserName є обов'язковим для збереження кошика." });
        }

        await _store.SaveAsync(cart);

        var cacheKey = $"basket:{cart.UserName.ToLowerInvariant()}";
        await _cache.SetAsync(cacheKey, cart, tags: ["baskets", $"user:{cart.UserName}"], cancellationToken: cancellationToken);

        _logger.LogInformation("💾 [Cache UPDATE] Кошик для '{User}' збережено та оновлено в HybridCache (L1 + L2).", cart.UserName);
        return Ok(cart);
    }

    /// <summary>
    /// Видалити кошик користувача зі сховища та кешу.
    /// </summary>
    /// <param name="userName">Ім'я користувача.</param>
    /// <param name="cancellationToken">Токен скасування.</param>
    [HttpDelete("{userName}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteBasket(string userName, CancellationToken cancellationToken)
    {
        var removed = await _store.DeleteAsync(userName);
        if (!removed)
        {
            return NotFound(new { Message = $"Кошик для '{userName}' не знайдено." });
        }

        var cacheKey = $"basket:{userName.ToLowerInvariant()}";
        await _cache.RemoveAsync(cacheKey, cancellationToken);

        _logger.LogInformation("🗑️ [Cache REMOVE] Кошик для '{User}' видалено зі сховища та очищено з HybridCache.", userName);
        return NoContent();
    }

    /// <summary>
    /// Примусово інвалідувати весь кеш кошиків за тегом 'baskets'.
    /// </summary>
    /// <param name="cancellationToken">Токен скасування.</param>
    [HttpPost("clear-cache")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ClearAllBasketCache(CancellationToken cancellationToken)
    {
        await _cache.RemoveByTagAsync("baskets", cancellationToken);
        _logger.LogWarning("🧹 [Cache INVALIDATE] Виконано ручне очищення кешу за тегом 'baskets'.");

        return Ok(new
        {
            Status = "CacheInvalidated",
            Tag = "baskets",
            Message = "Увесь кеш кошиків (L1 + L2) успішно скинуто за тегом 'baskets'."
        });
    }
}
