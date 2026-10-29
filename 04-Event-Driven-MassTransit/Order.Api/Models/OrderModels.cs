namespace Order.Api.Models;

public record CreateOrderRequest(
    string CustomerName,
    Guid ProductId,
    string ProductName,
    decimal Price,
    int Quantity);

public record OrderCreationResult(
    Guid OrderId,
    string Status,
    string CustomerName,
    string ProductName,
    decimal TotalAmount,
    DateTime DispatchedAtUtc);
