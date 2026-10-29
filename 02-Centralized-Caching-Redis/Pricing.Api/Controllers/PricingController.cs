using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Pricing.Api.Models;
using StackExchange.Redis;

namespace Pricing.Api.Controllers;

/// <summary>
/// Контролер управління цінами та акціями товарів.
/// Публікує події у Redis Pub/Sub канал 'pricing-channel',
/// що ініціює міжсервісну інвалідацію кешу в Basket.Api.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class PricingController : ControllerBase
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<PricingController> _logger;

    // Спільний стан каталогу цін для демонстрації
    private static readonly Dictionary<Guid, (string Name, decimal Price)> ActivePrices = new()
    {
        [Guid.Parse("11111111-1111-1111-1111-111111111111")] = ("Смарт-годинник Pro Pulse", 199.99m),
        [Guid.Parse("22222222-2222-2222-2222-222222222222")] = ("Бездротова миша UltraGlide 4K", 79.99m)
    };
    private static decimal CurrentDiscountPercent = 0m;

    public PricingController(IConnectionMultiplexer redis, ILogger<PricingController> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    /// <summary>
    /// Отримати перелік актуальних цін на товари та поточну діючу знижку.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(PricingOverviewResponse), StatusCodes.Status200OK)]
    public IActionResult GetPricingList()
    {
        var products = ActivePrices.Select(p => new ProductPriceDto(
            p.Key,
            p.Value.Name,
            p.Value.Price
        )).ToList();

        return Ok(new PricingOverviewResponse(CurrentDiscountPercent, products));
    }

    /// <summary>
    /// Змінити ціну на товар та сповістити всі зацікавлені мікросервіси через Redis Pub/Sub.
    /// </summary>
    /// <param name="request">Ідентифікатор товару, нова ціна та причина зміни.</param>
    [HttpPost("update-price")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdatePrice([FromBody] UpdatePriceRequest request)
    {
        if (!ActivePrices.ContainsKey(request.ProductId))
        {
            return NotFound(new { Message = $"Товар з ID {request.ProductId} не знайдено в каталозі цін." });
        }

        var oldPrice = ActivePrices[request.ProductId].Price;
        ActivePrices[request.ProductId] = (ActivePrices[request.ProductId].Name, request.NewPrice);

        _logger.LogInformation("🏷️ [PricingController] Зміна ціни для {ProductId}: {OldPrice:C} -> {NewPrice:C} (Причина: {Reason})",
            request.ProductId, oldPrice, request.NewPrice, request.Reason);

        // Публікуємо повідомлення у Redis Pub/Sub канал 'pricing-channel'
        var sub = _redis.GetSubscriber();
        var payload = JsonSerializer.Serialize(new
        {
            EventType = "PriceUpdatedEvent",
            request.ProductId,
            request.NewPrice,
            request.Reason,
            TimestampUtc = DateTime.UtcNow
        });

        await sub.PublishAsync(RedisChannel.Literal("pricing-channel"), payload);

        _logger.LogInformation("📢 [PricingController] Подія PriceUpdatedEvent відправлена в Redis Pub/Sub канал 'pricing-channel'.");

        return Ok(new
        {
            Status = "PriceUpdatedAndPublished",
            request.ProductId,
            OldPrice = oldPrice,
            request.NewPrice,
            Channel = "pricing-channel",
            Message = "Подія відправлена. Basket.Api автоматично інвалідує свій HybridCache!"
        });
    }

    /// <summary>
    /// Застосувати глобальну промо-знижку на всі замовлення та сповістити сервіс кошика через Redis Pub/Sub.
    /// </summary>
    /// <param name="request">Промокод та відсоток знижки.</param>
    [HttpPost("apply-discount")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ApplyDiscount([FromBody] ApplyDiscountRequest request)
    {
        CurrentDiscountPercent = request.DiscountPercent;

        _logger.LogInformation("🎉 [PricingController] Застосовано знижку '{Code}' ({Percent}%). Сповіщаємо підписників...",
            request.DiscountCode, request.DiscountPercent);

        var sub = _redis.GetSubscriber();
        var payload = JsonSerializer.Serialize(new
        {
            EventType = "DiscountAppliedEvent",
            request.DiscountCode,
            request.DiscountPercent,
            TimestampUtc = DateTime.UtcNow
        });

        await sub.PublishAsync(RedisChannel.Literal("pricing-channel"), payload);

        return Ok(new
        {
            Status = "DiscountApplied",
            request.DiscountCode,
            request.DiscountPercent,
            Channel = "pricing-channel"
        });
    }
}
