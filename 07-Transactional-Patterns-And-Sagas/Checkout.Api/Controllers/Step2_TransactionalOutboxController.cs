using System.Text.Json;
using Checkout.Api.BackgroundServices;
using Checkout.Api.Data;
using Checkout.Api.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Checkout.Api.Controllers;

/// <summary>
/// 🛡️ КРОК 2: ПАТЕРН TRANSACTIONAL OUTBOX + POLLY v8
/// 
/// 💡 ЯК OUTBOX ВИРІШУЄ DUAL-WRITE?
/// Замість спроби говорити з двома різними системами (БД та Брокер), ми зберігаємо 
/// повідомлення ЯК ЗВИЧАЙНИЙ РЯДОК у таблиці 'OutboxMessages' тієї ж самої бази даних!
/// 
/// Завдяки ACID-транзакціям SQL:
///   1. Додаємо рядок Order.
///   2. Додаємо рядок OutboxMessage.
///   3. Викликаємо SaveChangesAsync() -> АБО ЗБЕРЕЖЕНО ОБИДВА, АБО ЖОДНОГО!
/// 
/// Потім окремий фоновий процес (OutboxPublisherWorker) спокійно читає цю таблицю
/// та надійно надсилає події з повторними спробами (Retry + Circuit Breaker через Polly).
/// </summary>
[ApiController]
[Route("api/step2-outbox")]
public class Step2_TransactionalOutboxController : ControllerBase
{
    private readonly AppDbContext _dbContext;

    public Step2_TransactionalOutboxController(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>
    /// Атомарне оформлення замовлення: замовлення та подія записуються в єдиній SQL-транзакції.
    /// </summary>
    [HttpPost("atomic-checkout")]
    public async Task<IActionResult> AtomicCheckout([FromBody] AtomicCheckoutRequest request)
    {
        var order = new Order
        {
            CustomerId = request.CustomerId,
            TotalAmount = request.TotalAmount,
            Status = "Created"
        };

        var orderCreatedPayload = new
        {
            OrderId = order.Id,
            order.CustomerId,
            order.TotalAmount,
            Timestamp = DateTime.UtcNow
        };

        var outboxMessage = new OutboxMessage
        {
            EventType = "OrderCreatedIntegrationEvent",
            Payload = JsonSerializer.Serialize(orderCreatedPayload),
            CreatedAt = DateTime.UtcNow
        };

        // Атомарне додавання в рамках одного контексту EF Core
        _dbContext.Orders.Add(order);
        _dbContext.OutboxMessages.Add(outboxMessage);
        await _dbContext.SaveChangesAsync();

        return Accepted(new
        {
            Preamble = "Атомарний запис виконано! Dual-write неможливий.",
            OrderId = order.Id,
            OutboxMessageId = outboxMessage.Id,
            Status = "GuaranteedAtLeastOnceDelivery",
            Explanation = "Дані збережено в БД. Фоновий воркер OutboxPublisherWorker підхопить запис і надішле його в брокер протягом 2 секунд."
        });
    }

    /// <summary>
    /// Переглянути стан повідомлень у таблиці Outbox (оброблені vs очікуючі).
    /// </summary>
    [HttpGet("messages")]
    public async Task<IActionResult> GetOutboxMessages()
    {
        var messages = await _dbContext.OutboxMessages
            .OrderByDescending(m => m.CreatedAt)
            .Take(25)
            .Select(m => new
            {
                m.Id,
                m.EventType,
                m.CreatedAt,
                m.ProcessedAt,
                IsDelivered = m.ProcessedAt != null,
                m.RetryCount,
                m.ErrorMessage
            })
            .ToListAsync();

        return Ok(new
        {
            TotalFound = messages.Count,
            Messages = messages
        });
    }

    /// <summary>
    /// Перемкнути режим симуляції аварії брокера для тестування Polly v8 (Retry + Circuit Breaker).
    /// </summary>
    [HttpPost("toggle-broker-outage")]
    public IActionResult ToggleBrokerOutage([FromQuery] bool isOutageActive)
    {
        OutboxPublisherWorker.SimulateBrokerOutage = isOutageActive;

        return Ok(new
        {
            SimulateBrokerOutage = isOutageActive,
            Message = isOutageActive
                ? "⚠️ Режим аварії брокера увімкнено! Спостерігайте в логах за спрацюванням Polly Retry та Circuit Breaker."
                : "✅ Режим аварії вимкнено! Polly Circuit Breaker повернеться в стан Closed, і повідомлення будуть доставлені."
        });
    }
}

public record AtomicCheckoutRequest(string CustomerId, decimal TotalAmount);
