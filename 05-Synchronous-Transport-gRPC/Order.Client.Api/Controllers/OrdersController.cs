using System.Diagnostics;
using Grpc.Core;
using Inventory.Grpc.Protos;
using Microsoft.AspNetCore.Mvc;
using Order.Client.Api.Models;

namespace Order.Client.Api.Controllers;

/// <summary>
/// Контролер клієнтських замовлень, що демонструє високошвидкісну синхронну взаємодію
/// через бінарний gRPC-клієнт з автоматичним Service Discovery у .NET Aspire.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class OrdersController : ControllerBase
{
    private readonly InventoryService.InventoryServiceClient _client;
    private readonly ILogger<OrdersController> _logger;

    public OrdersController(InventoryService.InventoryServiceClient client, ILogger<OrdersController> logger)
    {
        _client = client;
        _logger = logger;
    }

    /// <summary>
    /// Unary RPC: Швидка перевірка залишку товару на складі через gRPC з вимірюванням часу відповіді (RTT).
    /// </summary>
    /// <param name="productId">Ідентифікатор товару (наприклад: '11111111-1111-1111-1111-111111111111').</param>
    [HttpGet("check-stock/{productId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> CheckStock(string productId)
    {
        var sw = Stopwatch.StartNew();
        _logger.LogInformation("📞 [Order.Client] Виклик gRPC GetProductStock для {ProductId}...", productId);

        // =========================================================================
        // КРИТИЧНА ТОЧКА 1: Високопродуктивний Unary виклик через HTTP/2
        // -------------------------------------------------------------------------
        // 1. Компактність: Protobuf кодує корисне навантаження у двійковий вигляд
        //    (замість текстових ключів JSON передаються однобайтові теги полів).
        // 2. Мультиплексування: клієнт gRPC повторно використовує ОДНЕ постійне
        //    TCP-з'єднання HTTP/2 для паралельних запитів без витрат на повторні рукостискання.
        // 3. Aspire Service Discovery ("https+http://inventory-grpc") динамічно транслює
        //    віртуальне ім'я у реальний порт сервера Kestrel.
        // =========================================================================
        var response = await _client.GetProductStockAsync(new ProductStockRequest
        {
            ProductId = productId
        });

        sw.Stop();
        _logger.LogInformation("⚡ [Order.Client] Відповідь отримана за {ElapsedMs}ms: {Product}, Наявно: {Stock}, Ціна: {Price:C}",
            sw.ElapsedMilliseconds, response.ProductName, response.AvailableQuantity, response.UnitPrice);

        return Ok(new
        {
            RoundTripTimeMs = sw.ElapsedMilliseconds,
            response.ProductId,
            response.ProductName,
            response.AvailableQuantity,
            response.UnitPrice,
            response.InStock
        });
    }

    /// <summary>
    /// Unary RPC: Атомарне резервування товару на складі через gRPC.
    /// </summary>
    /// <param name="request">Ідентифікатор товару та необхідна кількість.</param>
    [HttpPost("reserve")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ReserveStock([FromBody] ReserveItemRequest request)
    {
        _logger.LogInformation("🔒 [Order.Client] Виклик gRPC ReserveStock для {ProductId}, кількість: {Qty}...",
            request.ProductId, request.Quantity);

        var response = await _client.ReserveStockAsync(new ReserveStockRequest
        {
            ProductId = request.ProductId,
            Quantity = request.Quantity,
            OrderId = Guid.NewGuid().ToString()
        });

        if (response.Success)
        {
            _logger.LogInformation("✅ [Order.Client] Успішна резервація! Код: {ResId}, Залишок: {Rem}",
                response.ReservationId, response.RemainingStock);

            return Ok(new
            {
                response.Success,
                response.Message,
                response.ReservationId,
                response.RemainingStock
            });
        }

        _logger.LogWarning("❌ [Order.Client] Відмова у резервації: {Msg}", response.Message);
        return BadRequest(new
        {
            response.Success,
            response.Message,
            response.RemainingStock
        });
    }

    /// <summary>
    /// Server Streaming RPC: Отримання неперервного потоку статусів для переліку товарів через одне з'єднання HTTP/2.
    /// </summary>
    /// <param name="request">Список ідентифікаторів товарів.</param>
    [HttpPost("stream-stock")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> StreamStock([FromBody] StreamStockRequest request)
    {
        _logger.LogInformation("🌊 [Order.Client] Початок Server Streaming gRPC для {Count} товарів...", request.ProductIds.Count);

        var streamRequest = new StockStreamRequest();
        streamRequest.ProductIds.AddRange(request.ProductIds);

        using var call = _client.GetStockStream(streamRequest);
        var results = new List<object>();

        // =========================================================================
        // КРИТИЧНА ТОЧКА 2: Server Streaming (Асинхронне читання потоку частинами)
        // -------------------------------------------------------------------------
        // Сервер повертає елементи послідовно через один постійний потік HTTP/2.
        // Клієнт зчитує кожен надісланий фрейм даних у міру його надходження
        // через 'await foreach (var item in call.ResponseStream.ReadAllAsync())',
        // не чекаючи формування всього підсумкового списку у пам'яті сервера.
        // =========================================================================
        await foreach (var item in call.ResponseStream.ReadAllAsync())
        {
            _logger.LogInformation("📥 [Stream Chunk] Отримано: {Product} (Наявно: {Stock})", item.ProductName, item.AvailableQuantity);
            results.Add(new
            {
                item.ProductId,
                item.ProductName,
                item.AvailableQuantity,
                item.UnitPrice,
                item.InStock
            });
        }

        return Ok(new
        {
            StreamCompleted = true,
            TotalItemsReceived = results.Count,
            Items = results
        });
    }
}
