using Contracts;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace Warehouse.Worker.Consumers;

/// <summary>
/// Обробник події OrderCreatedEvent.
/// Демонструє автоматичну обробку та механізм повторів (Retry) при збоях.
/// </summary>
public class OrderCreatedConsumer : IConsumer<OrderCreatedEvent>
{
    private readonly ILogger<OrderCreatedConsumer> _logger;

    public OrderCreatedConsumer(ILogger<OrderCreatedConsumer> logger)
    {
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<OrderCreatedEvent> context)
    {
        var msg = context.Message;
        _logger.LogInformation("📦 [OrderCreatedConsumer] Отримано замовлення {OrderId} від клієнта '{Customer}'. Товар: {Product} ({Qty} шт.), Сума: {Total:C}",
            msg.OrderId, msg.CustomerName, msg.ProductName, msg.Quantity, msg.TotalAmount);

        // ДЕМОНСТРАЦІЯ RETRY & DEAD LETTER:
        // Якщо передано ім'я з маркером "SimulateError", штучно кидаємо помилку
        if (msg.CustomerName.Contains("SimulateError", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogError("💥 [OrderCreatedConsumer] Імітація збою під час обробки замовлення {OrderId}! Спроба №{RetryAttempt}",
                msg.OrderId, context.GetRetryAttempt() + 1);

            throw new InvalidOperationException($"Штучна помилка обробки замовлення {msg.OrderId} для демонстрації політики Retry у MassTransit.");
        }

        // Імітація роботи резервування складу
        await Task.Delay(100);

        _logger.LogInformation("✅ [OrderCreatedConsumer] Замовлення {OrderId} успішно зарезервовано на складі!", msg.OrderId);
    }
}

/// <summary>
/// Обробник синхронного запиту через шину (Request/Response Pattern).
/// Відповідає на запит перевірки залишків без блокування HTTP-з'єднання.
/// </summary>
public class CheckStockConsumer : IConsumer<CheckStockRequest>
{
    private readonly ILogger<CheckStockConsumer> _logger;

    // Імітація залишків на складі
    private static readonly Dictionary<Guid, (string Name, int Quantity, decimal Price)> Inventory = new()
    {
        [Guid.Parse("11111111-1111-1111-1111-111111111111")] = ("Смарт-годинник Pro Pulse", 10, 199.99m),
        [Guid.Parse("22222222-2222-2222-2222-222222222222")] = ("Бездротова миша UltraGlide 4K", 2, 79.99m)
    };

    public CheckStockConsumer(ILogger<CheckStockConsumer> logger)
    {
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<CheckStockRequest> context)
    {
        var req = context.Message;
        _logger.LogInformation("🔍 [CheckStockConsumer] Запит наявності товару {ProductId}, кількість: {Qty}",
            req.ProductId, req.RequestedQuantity);

        if (Inventory.TryGetValue(req.ProductId, out var item))
        {
            if (item.Quantity >= req.RequestedQuantity)
            {
                var confirmation = $"STOCK-OK-{Guid.NewGuid().ToString()[..6].ToUpperInvariant()}";
                _logger.LogInformation("✅ [CheckStockConsumer] Товар є в наявності ({Current} шт.). Відправляємо StockAvailableResponse.", item.Quantity);

                // Відповідаємо через контекст запиту!
                await context.RespondAsync(new StockAvailableResponse(
                    req.ProductId,
                    item.Quantity,
                    item.Price,
                    confirmation));
                return;
            }

            _logger.LogWarning("⚠️ [CheckStockConsumer] Недостатньо залишку (доступно: {Current}, потрібно: {Req}). Відправляємо StockUnavailableResponse.",
                item.Quantity, req.RequestedQuantity);

            await context.RespondAsync(new StockUnavailableResponse(
                req.ProductId,
                req.RequestedQuantity,
                item.Quantity,
                $"Недостатньо залишку на складі. Доступно лише {item.Quantity} шт."));
            return;
        }

        _logger.LogError("❌ [CheckStockConsumer] Товар {ProductId} не знайдено на складі.", req.ProductId);
        await context.RespondAsync(new StockUnavailableResponse(
            req.ProductId,
            req.RequestedQuantity,
            0,
            "Товар відсутній у каталозі складу."));
    }
}

/// <summary>
/// Обробник аварійних подій (Fault Consumer).
/// Викликається MassTransit тоді, коли ВСІ спроби Retry вичерпалися,
/// дозволяючи залогувати критичну помилку або виконати компенсацію.
/// </summary>
public class OrderCreatedFaultConsumer : IConsumer<Fault<OrderCreatedEvent>>
{
    private readonly ILogger<OrderCreatedFaultConsumer> _logger;

    public OrderCreatedFaultConsumer(ILogger<OrderCreatedFaultConsumer> logger)
    {
        _logger = logger;
    }

    public Task Consume(ConsumeContext<Fault<OrderCreatedEvent>> context)
    {
        var fault = context.Message;
        _logger.LogCritical("💀 [FAULT CONSUMER] Замовлення {OrderId} остаточно впало після всіх повторів! Переміщено в чергу '_error'. Помилка: {Exceptions}",
            fault.Message.OrderId, string.Join("; ", fault.Exceptions.Select(e => e.Message)));

        return Task.CompletedTask;
    }
}
