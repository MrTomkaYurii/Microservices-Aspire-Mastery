using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileSystemGlobbing.Internal;
using RabbitMQ.Client;
using Shared.Messaging.Events;
using System.Text;
using System.Text.Json;

namespace Basket.Services;

public class BasketService(
                HybridCache cache, 
                BasketDbContext dbContext,
                // if ONLY RABBITMQ is used we need IConnection
                global::RabbitMQ.Client.IConnection rabbitConnection,
                // if Masstransit is used we need IBus
                IBus bus
    )
{
    public async Task<ShoppingCart?> GetBasket(string userName)
    {
        var basket = await cache.GetOrCreateAsync(userName, async token =>
        {
            return await dbContext.ShoppingCarts
                .Include(x => x.Items)
                .FirstOrDefaultAsync(x => x.UserName == userName, token);
        });

        return basket;
    }

    public async Task UpdateBasket(ShoppingCart shoppingCart)
    {
        // Before update(Add/remove Item) into SC, we should call Catalog ms GetProductById method
        // Get latest product information and set Price and ProductName when adding item into SC

        var existingBasket = await dbContext.ShoppingCarts
                .Include(x => x.Items)
                .FirstOrDefaultAsync(x => x.UserName == shoppingCart.UserName);

        if (existingBasket == null)
        {
            // new basket
            dbContext.ShoppingCarts.Add(shoppingCart);
            await dbContext.SaveChangesAsync();

            await cache.SetAsync(shoppingCart.UserName, shoppingCart);
        }
        else
        {
            //update existing basket
            existingBasket.Items = shoppingCart.Items;

            //// removed: not necessary for caching section
            //foreach (var item in existingBasket.Items)
            //{
            //    var product = await catalogApiClient.GetProductById(item.ProductId);
            //    item.Price = product.Price;
            //    item.ProductName = product.Name;
            //}

            dbContext.ShoppingCarts.Update(existingBasket);
            await dbContext.SaveChangesAsync();

            await cache.SetAsync(existingBasket.UserName, existingBasket);
        }        
    }





    public async Task CheckoutBasket(BasketCheckout basketCheckout)
    {
        // Get existing basket with total price
        // Set totalprice on basketcheckout event message
        // Send basket checkout event to rabbitmq using masstransit
        // Delete the basket

        #region CASE #01 - CHECKOUT BASKET WITH RABBITMQ CLIENT
        ////======================================================
        // Отримуємо поточну корзину асинхроно
        var shoppingCart = await GetBasket(basketCheckout.UserName);
        if (shoppingCart is null)
        {
            throw new InvalidOperationException($"Shopping cart for user '{basketCheckout.UserName}' not found.");
        }
        // Встановлюємо загальну вартість в повідомленні оформлення замовлення
        basketCheckout.TotalPrice = shoppingCart.TotalPrice;
        // Создаем событие интеграции
        var integrationEvent = new BasketCheckoutIntegrationEvent
        {
            UserName = basketCheckout.UserName,
            TotalPrice = shoppingCart.TotalPrice,
            FirstName = basketCheckout.FirstName,
            LastName = basketCheckout.LastName,
            EmailAddress = basketCheckout.EmailAddress,
            AddressLine = basketCheckout.AddressLine
        };
        // Асинхроно створюємо канал
        await using var channel = await rabbitConnection.CreateChannelAsync();
        // Оголошуємо exchange асинхроно (в v7+ вже є async-варіант)
        await channel.ExchangeDeclareAsync(
            exchange: "basket-checkout-exchange",
            type: ExchangeType.Fanout,
            durable: true,
            autoDelete: false
        );
        // Сереалізуємо подію в JSON
        var message = JsonSerializer.Serialize(integrationEvent);
        var body = Encoding.UTF8.GetBytes(message);
        // Налаштовуємо особливості повідомлення (в v7+ напряму через конструктор)
        var properties = new BasicProperties
        {
            ContentType = "application/json",
            DeliveryMode = DeliveryModes.Persistent, // аналог Persistent = true
            MessageId = Guid.NewGuid().ToString(),
            Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds())
        };
        // Асинхронно публікуємо подію (новий метод в v7+)
        await channel.BasicPublishAsync(
            exchange: "basket-checkout-exchange",
            routingKey: "",
            mandatory: false,
            basicProperties: properties,
            body: body
        );
        // Видаляємо корзину асинхронно
        await DeleteBasket(basketCheckout.UserName);
        #endregion


        #region CASE #2 - CHECKOUT BASKET WITH MASSTRANSIT
        //// =====================================================================
        //Get existing basket with total price
        //var shoppingCart = await GetBasket(basketCheckout.UserName);
        //if (shoppingCart is null)
        //{
        //    throw new InvalidOperationException($"Shopping cart for user '{basketCheckout.UserName}' not found.");
        //}
        //// Set total price on basket checkout event message
        //basketCheckout.TotalPrice = shoppingCart.TotalPrice;
        //// Send basket checkout event to rabbitmq using masstransit
        //var integrationEvent = new BasketCheckoutIntegrationEvent
        //{
        //    UserName = basketCheckout.UserName,
        //    TotalPrice = shoppingCart.TotalPrice,
        //    FirstName = basketCheckout.FirstName,
        //    LastName = basketCheckout.LastName,
        //    EmailAddress = basketCheckout.EmailAddress,
        //    AddressLine = basketCheckout.AddressLine
        //};
        //// Publish checkout basket event and create order
        //await bus.Publish(integrationEvent);
        //// Delete the basket
        //await DeleteBasket(basketCheckout.UserName);
        #endregion


        #region CASE №3 - OUTBOX PATTERN
        //// =====================================================================
        //await dbContext.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        //{
        //    await using var transaction = await dbContext.Database.BeginTransactionAsync();
        //    try
        //    {
        //        // Get existing basket with total price
        //        var shoppingCart = await dbContext.ShoppingCarts
        //          .Include(x => x.Items)
        //          .FirstOrDefaultAsync(x => x.UserName == basketCheckout.UserName);

        //        if (shoppingCart is null)
        //        {
        //            throw new InvalidOperationException($"Shopping cart for user '{basketCheckout.UserName}' not found.");
        //        }

        //        // Set total price on basket checkout event message
        //        basketCheckout.TotalPrice = shoppingCart.TotalPrice;

        //        var integrationEvent = new BasketCheckoutIntegrationEvent
        //        {
        //            UserName = basketCheckout.UserName,
        //            TotalPrice = shoppingCart.TotalPrice,
        //            FirstName = basketCheckout.FirstName,
        //            LastName = basketCheckout.LastName,
        //            EmailAddress = basketCheckout.EmailAddress,
        //            AddressLine = basketCheckout.AddressLine
        //        };

        //        // Write a message to the outbox
        //        var outboxMessage = new OutboxMessage
        //        {
        //            Id = Guid.NewGuid(),
        //            Type = typeof(BasketCheckoutIntegrationEvent).AssemblyQualifiedName!,
        //            Content = JsonSerializer.Serialize(integrationEvent),
        //            OccuredOn = DateTime.UtcNow
        //        };
        //        dbContext.OutboxMessages.Add(outboxMessage);

        //        // Delete the basket            
        //        dbContext.ShoppingCarts.Remove(shoppingCart);

        //        // Commit the transaction
        //        await dbContext.SaveChangesAsync();
        //        await transaction.CommitAsync();

        //        // Invalidate the Redis cache
        //        await cache.RemoveAsync(basketCheckout.UserName);
        //    }
        //    catch
        //    {
        //        await transaction.RollbackAsync();
        //        //throw;
        //    }
        //});
        #endregion
    }





    public async Task DeleteBasket(string userName)
    {
        await dbContext.ShoppingCarts
            .Where(x => x.UserName == userName)
            .ExecuteDeleteAsync();

        await cache.RemoveAsync(userName);
    }

    internal async Task UpdateBasketItemProductPrices(int productId, decimal price)
    {
        // IDistributedCache not supported list of keys function
        // https://github.com/dotnet/runtime/issues/36402

        var basket = await GetBasket("swn");

        if (basket == null) return;

        var item = basket!.Items.FirstOrDefault(x => x.ProductId == productId);
        if (item != null)
        {
            item.Price = price;

            //update db
            dbContext.ShoppingCarts.Update(basket);
            await dbContext.SaveChangesAsync();

            //update cache
            await cache.SetAsync(basket.UserName, basket);            
        }
    }
}
