using System.Collections.Concurrent;
using Grpc.Core;
using Inventory.Grpc.Protos;

namespace Inventory.Grpc.Services;

public class InventoryGrpcServiceImpl : InventoryService.InventoryServiceBase
{
    private readonly ILogger<InventoryGrpcServiceImpl> _logger;

    // Імітація бази даних складу товарів
    private static readonly ConcurrentDictionary<string, (string Name, int Stock, double Price)> Storage = new()
    {
        ["11111111-1111-1111-1111-111111111111"] = ("Смарт-годинник Pro Pulse", 50, 199.99),
        ["22222222-2222-2222-2222-222222222222"] = ("Бездротова миша UltraGlide 4K", 12, 79.99),
        ["33333333-3333-3333-3333-333333333333"] = ("Механічна клавіатура CyberClick RGB", 0, 129.50)
    };

    public InventoryGrpcServiceImpl(ILogger<InventoryGrpcServiceImpl> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Unary Call: швидке отримання залишку та ціни конкретного товару.
    /// Бінарна передача по HTTP/2 забезпечує мінімальні накладні витрати на серіалізацію.
    /// </summary>
    public override Task<ProductStockResponse> GetProductStock(ProductStockRequest request, ServerCallContext context)
    {
        _logger.LogInformation("⚡ [gRPC Server] Запит залишку для товару {ProductId}", request.ProductId);

        if (Storage.TryGetValue(request.ProductId, out var item))
        {
            return Task.FromResult(new ProductStockResponse
            {
                ProductId = request.ProductId,
                ProductName = item.Name,
                AvailableQuantity = item.Stock,
                UnitPrice = item.Price,
                InStock = item.Stock > 0
            });
        }

        return Task.FromResult(new ProductStockResponse
        {
            ProductId = request.ProductId,
            ProductName = "Невідомий товар",
            AvailableQuantity = 0,
            UnitPrice = 0,
            InStock = false
        });
    }

    /// <summary>
    /// Unary Call: атомарна резервація залишку на складі.
    /// </summary>
    public override Task<ReserveStockResponse> ReserveStock(ReserveStockRequest request, ServerCallContext context)
    {
        _logger.LogInformation("🔒 [gRPC Server] Спроба резервування: товар {ProductId}, кількість: {Qty}, замовлення: {OrderId}",
            request.ProductId, request.Quantity, request.OrderId);

        if (!Storage.TryGetValue(request.ProductId, out var item))
        {
            return Task.FromResult(new ReserveStockResponse
            {
                Success = false,
                Message = $"Товар з ID {request.ProductId} не знайдено на складі.",
                ReservationId = string.Empty,
                RemainingStock = 0
            });
        }

        if (item.Stock < request.Quantity)
        {
            _logger.LogWarning("⚠️ [gRPC Server] Недостатній залишок (наявно: {Stock}, запитано: {Qty})", item.Stock, request.Quantity);

            return Task.FromResult(new ReserveStockResponse
            {
                Success = false,
                Message = $"Недостатній залишок на складі. Доступно: {item.Stock} шт.",
                ReservationId = string.Empty,
                RemainingStock = item.Stock
            });
        }

        var newStock = item.Stock - request.Quantity;
        Storage[request.ProductId] = (item.Name, newStock, item.Price);

        var reservationId = $"RES-{Guid.NewGuid().ToString()[..8].ToUpperInvariant()}";
        _logger.LogInformation("✅ [gRPC Server] Товар зарезервовано! ReservationId: {ResId}, залишилось: {Remaining}", reservationId, newStock);

        return Task.FromResult(new ReserveStockResponse
        {
            Success = true,
            Message = "Товар успішно зарезервовано.",
            ReservationId = reservationId,
            RemainingStock = newStock
        });
    }

    /// <summary>
    /// Server Streaming Call: повертає потік статусів по списку товарів у реальному часі.
    /// </summary>
    public override async Task GetStockStream(StockStreamRequest request, IServerStreamWriter<ProductStockResponse> responseStream, ServerCallContext context)
    {
        _logger.LogInformation("🌊 [gRPC Server] Запуск серверного стрімінгу для {Count} товарів...", request.ProductIds.Count);

        foreach (var productId in request.ProductIds)
        {
            if (context.CancellationToken.IsCancellationRequested)
                break;

            if (Storage.TryGetValue(productId, out var item))
            {
                await responseStream.WriteAsync(new ProductStockResponse
                {
                    ProductId = productId,
                    ProductName = item.Name,
                    AvailableQuantity = item.Stock,
                    UnitPrice = item.Price,
                    InStock = item.Stock > 0
                });

                // Імітуємо невелику затримку для наочності стрімінгу
                await Task.Delay(50, context.CancellationToken);
            }
        }

        _logger.LogInformation("🏁 [gRPC Server] Серверний стрім завершено.");
    }
}
