namespace Basket.Api.Models;

public record ShoppingCartItem(
    Guid ProductId,
    string ProductName,
    decimal UnitPrice,
    int Quantity)
{
    public decimal TotalItemPrice => UnitPrice * Quantity;
}

public class ShoppingCart
{
    public string UserName { get; set; } = string.Empty;
    public List<ShoppingCartItem> Items { get; set; } = [];
    public decimal DiscountPercent { get; set; } = 0m;

    public decimal SubtotalPrice => Items.Sum(i => i.TotalItemPrice);
    public decimal TotalPrice => SubtotalPrice * (1.0m - (DiscountPercent / 100.0m));
    public DateTime LastUpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public record PriceUpdatedEvent(Guid ProductId, decimal NewPrice, string Reason);
public record DiscountAppliedEvent(string DiscountCode, decimal DiscountPercent);
