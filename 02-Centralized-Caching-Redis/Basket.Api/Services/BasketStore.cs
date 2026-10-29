using System.Collections.Concurrent;
using Basket.Api.Models;

namespace Basket.Api.Services;

/// <summary>
/// Імітація постійного сховища (Backing Database) для кошиків користувачів.
/// HybridCache використовується поверх цього сховища для запобігання надмірним запитам.
/// </summary>
public class BasketStore
{
    private readonly ConcurrentDictionary<string, ShoppingCart> _baskets = new(StringComparer.OrdinalIgnoreCase);

    public BasketStore()
    {
        // Початковий демо-кошик для користувача "john"
        var sampleCart = new ShoppingCart
        {
            UserName = "john",
            Items =
            [
                new(Guid.Parse("11111111-1111-1111-1111-111111111111"), "Смарт-годинник Pro Pulse", 199.99m, 1),
                new(Guid.Parse("22222222-2222-2222-2222-222222222222"), "Бездротова миша UltraGlide 4K", 79.99m, 2)
            ]
        };

        _baskets["john"] = sampleCart;
    }

    public Task<ShoppingCart?> GetAsync(string userName)
    {
        _baskets.TryGetValue(userName, out var cart);
        return Task.FromResult(cart);
    }

    public Task SaveAsync(ShoppingCart cart)
    {
        cart.LastUpdatedAtUtc = DateTime.UtcNow;
        _baskets[cart.UserName] = cart;
        return Task.CompletedTask;
    }

    public Task<bool> DeleteAsync(string userName)
    {
        return Task.FromResult(_baskets.TryRemove(userName, out _));
    }

    public IEnumerable<ShoppingCart> GetAll() => _baskets.Values;

    public void UpdateProductPrice(Guid productId, decimal newPrice)
    {
        foreach (var cart in _baskets.Values)
        {
            var item = cart.Items.FirstOrDefault(i => i.ProductId == productId);
            if (item != null)
            {
                cart.Items.Remove(item);
                cart.Items.Add(new ShoppingCartItem(item.ProductId, item.ProductName, newPrice, item.Quantity));
                cart.LastUpdatedAtUtc = DateTime.UtcNow;
            }
        }
    }

    public void ApplyGlobalDiscount(decimal discountPercent)
    {
        foreach (var cart in _baskets.Values)
        {
            cart.DiscountPercent = discountPercent;
            cart.LastUpdatedAtUtc = DateTime.UtcNow;
        }
    }
}
