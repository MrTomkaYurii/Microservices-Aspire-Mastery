using Microsoft.AspNetCore.Mvc;
using Ordering.Models;
using Ordering.Services;

namespace Ordering.Controllers;

/// <summary>
/// Контролер керування замовленнями eShop (SQL Server + EF Core + Transactional Outbox).
/// Підтримує як стандартний роут /orders (для YARP API Gateway), так і /api/orders.
/// </summary>
[ApiController]
[Route("orders")]
[Route("api/[controller]")]
[Produces("application/json")]
public class OrdersController : ControllerBase
{
    private readonly OrderService _orderService;
    private readonly ILogger<OrdersController> _logger;

    public OrdersController(OrderService orderService, ILogger<OrdersController> logger)
    {
        _orderService = orderService;
        _logger = logger;
    }

    /// <summary>
    /// Отримати список усіх замовлень у системі.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<Order>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAllOrders()
    {
        _logger.LogInformation("Отримання списку всіх замовлень");
        var orders = await _orderService.GetAllOrdersAsync();
        return Ok(orders);
    }

    /// <summary>
    /// Отримати всі замовлення конкретного покупця за userName.
    /// </summary>
    [HttpGet("{userName}")]
    [ProducesResponseType(typeof(IEnumerable<Order>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetOrdersByUserName(string userName)
    {
        _logger.LogInformation("Отримання замовлень для користувача {UserName}", userName);
        var orders = await _orderService.GetOrdersByUserNameAsync(userName);
        if (orders is null || !orders.Any())
        {
            return NotFound(new { Message = $"Замовлень для користувача '{userName}' не знайдено." });
        }

        return Ok(orders);
    }

    /// <summary>
    /// Створити нове замовлення безпосередньо через API (наприклад, під час тестування).
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(Order), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateOrder([FromBody] Order order)
    {
        if (order is null)
        {
            return BadRequest(new { Message = "Дані замовлення не можуть бути порожніми." });
        }

        _logger.LogInformation("Створення замовлення для користувача {UserName} на суму {TotalPrice}", order.UserName, order.TotalPrice);
        await _orderService.CreateOrderAsync(order);
        return CreatedAtAction(nameof(GetOrdersByUserName), new { userName = order.UserName }, order);
    }
}
