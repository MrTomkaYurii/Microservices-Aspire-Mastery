using Contracts;
using MassTransit;
using Microsoft.AspNetCore.Mvc;
using Order.Api.Models;

namespace Order.Api.Controllers;

/// <summary>
/// Контролер замовлень у подієво-орієнтованій архітектурі (Event-Driven Architecture).
/// Демонструє публікацію подій (Publish/Subscribe) та типізований запит-відповідь через шину (Request/Response).
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class OrdersController : ControllerBase
{
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly IRequestClient<CheckStockRequest> _requestClient;
    private readonly ILogger<OrdersController> _logger;

    public OrdersController(
        IPublishEndpoint publishEndpoint,
        IRequestClient<CheckStockRequest> requestClient,
        ILogger<OrdersController> logger)
    {
        _publishEndpoint = publishEndpoint;
        _requestClient = requestClient;
        _logger = logger;
    }

    /// <summary>
    /// Створити нове замовлення та асинхронно опублікувати подію 'OrderCreatedEvent' у шину MassTransit.
    /// Передайте 'SimulateError' у поле CustomerName для демонстрації Retry Policy та черги Fault.
    /// </summary>
    /// <param name="request">Параметри замовлення.</param>
    /// <param name="cancellationToken">Токен скасування.</param>
    [HttpPost]
    [ProducesResponseType(typeof(OrderCreationResult), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateOrder([FromBody] CreateOrderRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.CustomerName))
        {
            return BadRequest(new { Message = "CustomerName є обов'язковим." });
        }

        var orderId = Guid.NewGuid();
        var totalAmount = request.Price * request.Quantity;

        var orderEvent = new OrderCreatedEvent(
            orderId,
            request.CustomerName,
            request.ProductId,
            request.ProductName,
            totalAmount,
            request.Quantity,
            DateTime.UtcNow);

        // =========================================================================
        // КРИТИЧНА ТОЧКА 1: Патерн Publish/Subscribe vs Point-to-Point Send
        // -------------------------------------------------------------------------
        // Метод _publishEndpoint.Publish<T>() публікує подію у Fanout-біржу за типом контракту
        // ('Contracts:OrderCreatedEvent'). Видавець (Order.Api) повністю розв'язаний з одержувачами:
        // він НЕ знає, скільки сервісів (Склад, Оплата, Нотифікації, CRM) підписано на цю подію.
        // Будь-який новий підписник може підключитися пізніше без модифікації коду Order.Api.
        // =========================================================================
        await _publishEndpoint.Publish(orderEvent, cancellationToken);

        _logger.LogInformation("🚀 [Order.Api] Опубліковано подію OrderCreatedEvent для замовлення {OrderId} (Клієнт: {Customer})",
            orderId, request.CustomerName);

        var result = new OrderCreationResult(
            orderId,
            "AcceptedForProcessing",
            request.CustomerName,
            request.ProductName,
            totalAmount,
            DateTime.UtcNow);

        return Accepted($"/api/orders/{orderId}", result);
    }

    /// <summary>
    /// Синхронний діалог через брокер (Request/Response Pattern): перевірка залишків без блокування HTTP-потоків.
    /// </summary>
    /// <param name="productId">Ідентифікатор товару на складі.</param>
    /// <param name="quantity">Необхідна кількість (за замовчуванням: 1).</param>
    /// <param name="cancellationToken">Токен скасування.</param>
    [HttpGet("check-stock/{productId:guid}")]
    [ProducesResponseType(typeof(StockAvailableResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(StockUnavailableResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CheckStock(Guid productId, [FromQuery] int quantity = 1, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("🔍 [Order.Api] Відправка CheckStockRequest для товару {ProductId} через шину...", productId);

        // =========================================================================
        // КРИТИЧНА ТОЧКА 2: Асинхронний діалог Request/Response через шину без блокування HTTP
        // -------------------------------------------------------------------------
        // IRequestClient<CheckStockRequest> відправляє повідомлення у чергу складу.
        // MassTransit автоматично створює тимчасову чергу відповідей (ReplyTo) та генерує
        // унікальний CorrelationId для кореляції запиту й відповіді.
        // HTTP-потік веб-сервера Kestrel залишається повністю вільним (не блокується очікуванням).
        // Кортеж (available, unavailable) дозволяє надійно обробити поліморфний результат.
        // =========================================================================
        var (available, unavailable) = await _requestClient.GetResponse<StockAvailableResponse, StockUnavailableResponse>(
            new CheckStockRequest(productId, quantity), cancellationToken);

        if (available.IsCompletedSuccessfully)
        {
            var response = await available;
            _logger.LogInformation("✅ [Order.Api] Отримано підтвердження наявності: {ConfirmCode}", response.Message.ConfirmationCode);
            return Ok(response.Message);
        }

        var failResponse = await unavailable;
        _logger.LogWarning("⚠️ [Order.Api] Отримано відмову наявності: {Reason}", failResponse.Message.Reason);
        return BadRequest(failResponse.Message);
    }
}
