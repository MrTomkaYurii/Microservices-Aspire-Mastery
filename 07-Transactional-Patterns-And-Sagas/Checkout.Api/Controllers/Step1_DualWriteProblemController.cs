using Checkout.Api.Data;
using Checkout.Api.Entities;
using Microsoft.AspNetCore.Mvc;

namespace Checkout.Api.Controllers;

/// <summary>
/// 💥 КРОК 1: ДЕМОНСТРАЦІЯ ПРОБЛЕМИ ПОДВІЙНОГО ЗАПИСУ (DUAL-WRITE PROBLEM)
/// 
/// 💡 В ЧОМУ СУТЬ ПРОБЛЕМИ?
/// У розподіленій системі сервіс часто повинен зробити ДВІ дії одночасно:
/// 1. Зберегти стан у реляційній базі даних (SQL).
/// 2. Опублікувати подію в брокер повідомлень (RabbitMQ).
/// 
/// ❌ ЧОМУ ЗВИЧАЙНИЙ КОД ПРИЗВОДИТЬ ДО КАТАСТРОФИ:
/// 
/// Сценарій А:
///   await dbContext.SaveChangesAsync(); // Замовлення збережено в БД!
///   // 💥 РАПТОВИЙ ЗБІЙ (Kill процесу, обрив мережі до RabbitMQ, помилка OOM)
///   await bus.Publish(new OrderCreatedEvent()); // Повідомлення НЕ надіслано!
///   -> Наслідок: Гроші з клієнта списано, замовлення є в БД, але Склад ніколи не отримає повідомлення і не відправить товар!
/// 
/// Сценарій Б:
///   await bus.Publish(new OrderCreatedEvent()); // Повідомлення в черзі!
///   // 💥 ЗБІЙ У БАЗІ ДАНИХ (Constraint violation, Deadlock)
///   await dbContext.SaveChangesAsync(); // Відкат транзакції!
///   -> Наслідок: Товар зі складу відвантажено, хоча замовлення в базі даних не існує!
/// </summary>
[ApiController]
[Route("api/step1-dual-write")]
public class Step1_DualWriteProblemController : ControllerBase
{
    private readonly AppDbContext _dbContext;

    public Step1_DualWriteProblemController(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>
    /// Симуляція збою Dual-Write: замовлення зберігається в БД, але зв'язок з брокером падає.
    /// </summary>
    [HttpPost("simulate-crash")]
    public async Task<IActionResult> SimulateDualWriteCrash([FromBody] DualWriteOrderRequest request)
    {
        // 1. Крок А: Успішно записуємо в БД
        var order = new Order
        {
            CustomerId = request.CustomerId,
            TotalAmount = request.TotalAmount,
            Status = "PendingPayment"
        };

        _dbContext.Orders.Add(order);
        await _dbContext.SaveChangesAsync();

        // 2. Крок Б: Симулюємо непередбачувану аварію перед публікацією в брокер
        if (request.SimulateBrokerFailure)
        {
            return StatusCode(500, new
            {
                Error = "💥 CRASH! Обрив з'єднання з RabbitMQ після збереження замовлення в БД!",
                OrderId = order.Id,
                DataCorruption = new
                {
                    DatabaseState = "Замовлення існує в базі даних зі статусом PendingPayment.",
                    BrokerState = "Подію OrderCreatedEvent НЕ було надіслано в RabbitMQ!",
                    Consequence = "Система перебуває в неузгодженому стані. Склад та бухгалтерія ніколи не дізнаються про це замовлення."
                },
                Solution = "Перейдіть до Кроку 2 (Transactional Outbox), де обидва записи об'єднуються в єдину транзакцію БД!"
            });
        }

        return Ok(new
        {
            Message = "Замовлення збережено і повідомлення надіслано (щасливий шлях без збоїв).",
            OrderId = order.Id
        });
    }
}

public record DualWriteOrderRequest(string CustomerId, decimal TotalAmount, bool SimulateBrokerFailure = true);
