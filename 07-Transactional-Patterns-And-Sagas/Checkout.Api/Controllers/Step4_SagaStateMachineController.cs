using Checkout.Api.Data;
using Checkout.Api.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Checkout.Api.Controllers;

/// <summary>
/// 🔄 КРОК 4: САГА ТА КОМПЕНСУЮЧІ ТРАНЗАКЦІЇ (SAGA PATTERN & COMPENSATIONS)
/// 
/// 💡 ЧОМУ РОЗПОДІЛЕНІ ТРАНЗАКЦІЇ (2PC) ЗАБОРОНЕНІ В МІКРОСЕРВІСАХ?
/// Two-Phase Commit (2PC) блокує рядки в базах даних різних сервісів через мережу. 
/// Якщо один сервіс завис або мережа моргнула — вся система встає колом.
/// 
/// 💡 ПАТЕРН SAGA:
/// Замість однієї великої транзакції, процес розбивається на ланцюжок локальних кроків:
///   1. Створити замовлення (Order.Api) -> Pending
///   2. Списати кошти (Payment.Api) -> Captured
///   3. Зарезервувати товар (Warehouse.Api) -> Reserved
///   4. Завершити замовлення -> Completed
/// 
/// 🔄 КОМПЕНСУЮЧІ ТРАНЗАКЦІЇ (ROLLBACK БЕЗ БЛОКУВАНЬ):
/// Якщо на кроці 3 виявилося, що товару немає на складі:
///   -> Система НЕ може зробити "SQL ROLLBACK" платежу, бо транзакція платежу ВЖЕ закоммічена!
///   -> Система запускає КОМПЕНСАЦІЮ: "Повернути кошти на карту клієнта" (RefundPayment) 
///      та переводить статус замовлення в 'CancelledDueToOutOfStock'.
/// </summary>
[ApiController]
[Route("api/step4-saga")]
public class Step4_SagaStateMachineController : ControllerBase
{
    private readonly AppDbContext _dbContext;

    public Step4_SagaStateMachineController(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>
    /// Запустити повноцінний цикл Саги для нового замовлення (Успішний шлях).
    /// </summary>
    [HttpPost("start-saga")]
    public async Task<IActionResult> StartSaga([FromBody] StartSagaRequest request)
    {
        var orderId = Guid.NewGuid();

        var sagaState = new OrderSagaState
        {
            CorrelationId = orderId,
            CustomerId = request.CustomerId,
            Amount = request.Amount,
            CurrentState = "Submitted",
            IsPaymentCaptured = false,
            IsStockReserved = false,
            UpdatedAt = DateTime.UtcNow
        };

        _dbContext.SagaStates.Add(sagaState);
        await _dbContext.SaveChangesAsync();

        // Симуляція Кроку 1 Саги: Списання коштів
        await Task.Delay(50);
        sagaState.IsPaymentCaptured = true;
        sagaState.CurrentState = "PaymentCaptured";
        await _dbContext.SaveChangesAsync();

        // Симуляція Кроку 2 Саги: Резервування складу
        await Task.Delay(50);
        sagaState.IsStockReserved = true;
        sagaState.CurrentState = "Completed";
        sagaState.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        return Ok(new
        {
            Preamble = "Сага успішно завершила весь життєвий цикл без помилок.",
            OrderId = orderId,
            FinalState = sagaState.CurrentState,
            AuditTrail = new[]
            {
                "Крок 1: OrderSubmitted (Замовлення створено)",
                "Крок 2: PaymentCaptured (Кошти авторизовано)",
                "Крок 3: StockReserved (Склад зарезервовано)",
                "Крок 4: Completed (Замовлення передано на доставку)"
            }
        });
    }

    /// <summary>
    /// Симуляція збою на етапі складу та виконання КОМПЕНСУЮЧОЇ транзакції (Повернення коштів).
    /// </summary>
    [HttpPost("simulate-compensation")]
    public async Task<IActionResult> SimulateSagaCompensation([FromBody] StartSagaRequest request)
    {
        var orderId = Guid.NewGuid();

        // Крок 1: Створення
        var sagaState = new OrderSagaState
        {
            CorrelationId = orderId,
            CustomerId = request.CustomerId,
            Amount = request.Amount,
            CurrentState = "Submitted",
            UpdatedAt = DateTime.UtcNow
        };
        _dbContext.SagaStates.Add(sagaState);
        await _dbContext.SaveChangesAsync();

        // Крок 2: Оплата пройшла успішно
        sagaState.IsPaymentCaptured = true;
        sagaState.CurrentState = "PaymentCaptured";
        await _dbContext.SaveChangesAsync();

        // Крок 3: ЗБІЙ НА СКЛАДІ (Товар розпродано!)
        sagaState.CurrentState = "StockReservationFailed";
        sagaState.FailureReason = "Товар відсутній на центральному складі.";
        await _dbContext.SaveChangesAsync();

        // 🔄 КОМПЕНСАЦІЯ: Повернення коштів назад клієнту
        await Task.Delay(60);
        sagaState.IsPaymentCaptured = false; // Гроші повернено
        sagaState.CurrentState = "Cancelled_PaymentRefunded";
        sagaState.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        return Ok(new
        {
            Preamble = "Спрацювала компенсуюча транзакція (Saga Compensation Rollback)!",
            OrderId = orderId,
            FinalState = sagaState.CurrentState,
            Explanation = "Оскільки SQL ROLLBACK неможливий між двома незалежними базами даних, Сага виконала зворотну дію: надіслала команду RefundPayment.",
            AuditTrail = new[]
            {
                "Крок 1: OrderSubmitted (Замовлення створено)",
                "Крок 2: PaymentCaptured (Кошти знято з клієнта)",
                "Крок 3: ПОМИЛКА: StockReservationFailed (Товару немає)",
                "Крок 4 (КОМПЕНСАЦІЯ): RefundPaymentExecuted (Гроші зараховано назад клієнту)",
                "Крок 5: Замовлення анульовано зі статусом Cancelled_PaymentRefunded"
            }
        });
    }

    /// <summary>
    /// Отримати поточний статус стану екземпляра Саги за OrderId.
    /// </summary>
    [HttpGet("saga-state/{orderId:guid}")]
    public async Task<IActionResult> GetSagaState(Guid orderId)
    {
        var state = await _dbContext.SagaStates.FirstOrDefaultAsync(s => s.CorrelationId == orderId);
        if (state == null)
        {
            return NotFound(new { Error = $"Сагу для OrderId {orderId} не знайдено." });
        }

        return Ok(state);
    }
}

public record StartSagaRequest(string CustomerId, decimal Amount);
