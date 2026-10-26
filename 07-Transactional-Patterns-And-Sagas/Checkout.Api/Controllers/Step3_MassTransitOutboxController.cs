using Checkout.Api.Data;
using Checkout.Api.Entities;
using MassTransit;
using Microsoft.AspNetCore.Mvc;

namespace Checkout.Api.Controllers;

/// <summary>
/// ⚙️ КРОК 3: ПРОМИСЛОВИЙ MASSTRANSIT NATIVE EF CORE OUTBOX
/// 
/// 💡 ЧОМУ ВАРТО ВИКОРИСТОВУВАТИ ГОТОВИЙ OUTBOX ВІД MASSTRANSIT?
/// Написаний вручну воркер (як у Кроці 2) чудовий для розуміння, але у продакшені виникають проблеми:
/// 1. Race Conditions: якщо підняти 10 реплік вашого сервісу, всі 10 одночасно виберуть одні й ті самі рядки з бази!
/// 2. Distributed Locks: потрібні складні розподілені блокування на рівні рядків SQL (FOR UPDATE SKIP LOCKED).
/// 3. Inbox Pattern: приймаюча сторона повинна вміти відсікати дублікати (Idempotent Consumer).
/// 
/// MassTransit 'AddEntityFrameworkOutbox' робить усе це автоматично:
/// - Ви просто пишете 'await _publishEndpoint.Publish(event)'.
/// - MassTransit перехоплює виклик і зберігає подію у своїй системній таблиці 'OutboxMessage'.
/// - Коли ви робите 'await dbContext.SaveChangesAsync()', подія і сутність фіксуються разом!
/// </summary>
[ApiController]
[Route("api/step3-masstransit-outbox")]
public class Step3_MassTransitOutboxController : ControllerBase
{
    private readonly AppDbContext _dbContext;
    private readonly IPublishEndpoint _publishEndpoint;

    public Step3_MassTransitOutboxController(AppDbContext dbContext, IPublishEndpoint publishEndpoint)
    {
        _dbContext = dbContext;
        _publishEndpoint = publishEndpoint;
    }

    /// <summary>
    /// Оформлення замовлення через вбудований Outbox MassTransit.
    /// </summary>
    [HttpPost("checkout")]
    public async Task<IActionResult> MassTransitCheckout([FromBody] MassTransitCheckoutRequest request)
    {
        var order = new Order
        {
            CustomerId = request.CustomerId,
            TotalAmount = request.TotalAmount,
            Status = "Processing"
        };

        // 1. Додаємо сутність до EF Core
        _dbContext.Orders.Add(order);

        // 2. Публікуємо подію через IPublishEndpoint.
        // Завдяки AddEntityFrameworkOutbox подія НЕ йде прямо в RabbitMQ зараз!
        // Вона записується в пам'ять DbContext як частина поточної транзакції.
        await _publishEndpoint.Publish(new OrderSubmittedEvent(
            order.Id,
            order.CustomerId,
            order.TotalAmount,
            DateTime.UtcNow
        ));

        // 3. Єдиний SaveChangesAsync коммітить Замовлення + Подію в базу даних!
        await _dbContext.SaveChangesAsync();

        return Ok(new
        {
            Preamble = "Подію перехоплено MassTransit EF Core Outbox.",
            OrderId = order.Id,
            Mechanism = "MassTransit автоматично відправить подію у відповідний exchange брокера після завершення транзакції БД.",
            InboxOutboxTables = "Таблиці 'OutboxMessage', 'OutboxState' та 'InboxState' керуються бібліотекою MassTransit без ручного коду."
        });
    }
}

public record MassTransitCheckoutRequest(string CustomerId, decimal TotalAmount);
public record OrderSubmittedEvent(Guid OrderId, string CustomerId, decimal Amount, DateTime CreatedAt);
