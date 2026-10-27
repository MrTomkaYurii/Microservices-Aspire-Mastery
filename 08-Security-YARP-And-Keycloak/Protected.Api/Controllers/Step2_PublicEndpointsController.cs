using Microsoft.AspNetCore.Mvc;

namespace Protected.Api.Controllers;

/// <summary>
/// 🌐 КРОК 2: ВІДКРИТІ ПУБЛІЧНІ ЕНДПОІНТИ (БЕЗ АВТОРИЗАЦІЇ)
/// 
/// 💡 НАВІЩО ПОТРІБНІ ВІДКРИТІ ЕНДПОІНТИ?
/// Навіть у захищеній мікросервісній системі частина ресурсів має бути доступна неавторизованим гостям:
/// - Каталог товарів для перегляду вітрини.
/// - Публічні рекламні акції.
/// - Інформація про версію сервісу та Health Checks для балансувальника.
/// </summary>
[ApiController]
[Route("api/step2-public")]
public class Step2_PublicEndpointsController : ControllerBase
{
    /// <summary>
    /// Отримати публічний каталог товарів (доступний гостям без JWT-токена).
    /// </summary>
    [HttpGet("catalog")]
    public IActionResult GetPublicCatalog()
    {
        return Ok(new
        {
            Message = "Публічні дані отримано успішно без авторизації.",
            Items = new[]
            {
                new { Id = 1, Name = "Ноутбук Dell XPS 15", Price = 1899.99, Stock = "Available" },
                new { Id = 2, Name = "Механічна клавіатура Keychron K2", Price = 99.50, Stock = "Available" },
                new { Id = 3, Name = "Бездротові навушники Sony WH-1000XM5", Price = 349.00, Stock = "LowStock" }
            },
            AccessLevel = "Anonymous / Public"
        });
    }
}
