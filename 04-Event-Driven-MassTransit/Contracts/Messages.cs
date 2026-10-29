namespace Contracts;

// =========================================================================
// 1. Подія (Event): факт, що вже стався у минулому часі.
//    Публікується через IPublishEndpoint.Publish<OrderCreatedEvent>()
//    і розсилається всім зацікавленим підписникам (Fanout / Topic).
// =========================================================================
public record OrderCreatedEvent(
    Guid OrderId,
    string CustomerName,
    Guid ProductId,
    string ProductName,
    decimal TotalAmount,
    int Quantity,
    DateTime CreatedAtUtc);

// =========================================================================
// 2. Запит (Request): синхронний діалог через шину (Request/Response Pattern).
//    Відправляється через IRequestClient<CheckStockRequest> без блокування HTTP.
// =========================================================================
public record CheckStockRequest(
    Guid ProductId,
    int RequestedQuantity);

// Варіант успішної відповіді
public record StockAvailableResponse(
    Guid ProductId,
    int AvailableQuantity,
    decimal UnitPrice,
    string ConfirmationCode);

// Варіант відмови (наприклад, недостатньо залишку на складі)
public record StockUnavailableResponse(
    Guid ProductId,
    int RequestedQuantity,
    int CurrentStock,
    string Reason);
