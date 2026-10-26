namespace Checkout.Api.Entities;

/// <summary>
/// 🔄 Стан екземпляра Саги (Saga Instance State)
/// Зберігає поточний крок розподіленої бізнес-транзакції та дані для можливої компенсації.
/// </summary>
public class OrderSagaState
{
    public Guid CorrelationId { get; set; } // OrderId
    public string CurrentState { get; set; } = "Submitted";
    public string CustomerId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public bool IsPaymentCaptured { get; set; }
    public bool IsStockReserved { get; set; }
    public string? FailureReason { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
