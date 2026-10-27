using Microsoft.AspNetCore.Mvc;

namespace Protected.Api.Controllers;

/// <summary>
/// 🛡️ КРОК 4: РОЛЬОВИЙ ДОСТУП ТА КЕРУВАННЯ ПРИВІЛЕЯМИ (RBAC & SCOPES)
/// 
/// 💡 РІЗНИЦЯ МІЖ АВТЕНТИФІКАЦІЄЮ ТА АВТОРИЗАЦІЄЮ:
/// - Автентифікація (Who are you?): Перевірка паспорта або JWT токена ("Це користувач Тарас Шевченко").
/// - Авторизація (What can you do?): Перевірка дозволів та ролей ("Чи дозволено Тарасу видаляти замовлення інших клієнтів?").
/// </summary>
[ApiController]
[Route("api/step4-rbac")]
public class Step4_RoleBasedAccessController : ControllerBase
{
    /// <summary>
    /// Ресурс звичайного покупця (вимагає ролі User або Admin).
    /// </summary>
    [HttpGet("orders")]
    public IActionResult GetUserOrders()
    {
        var role = Request.Headers["X-User-Role"].FirstOrDefault() ?? "Guest";

        if (role != "User" && role != "Admin" && role != "Manager")
        {
            return StatusCode(403, new
            {
                Error = "403 Forbidden: Недостатньо прав доступу.",
                RequiredRoles = new[] { "User", "Admin" },
                YourRole = role,
                Message = "Для доступу до приватних замовлень необхідно мати активну сесію покупця."
            });
        }

        return Ok(new
        {
            Message = "Список персональних замовлень користувача.",
            Orders = new[]
            {
                new { OrderId = "ORD-2026-001", Amount = 149.99, Status = "Delivered" },
                new { OrderId = "ORD-2026-002", Amount = 890.00, Status = "InTransit" }
            }
        });
    }

    /// <summary>
    /// Критичний адміністративний ресурс (вимагає ексклюзивної ролі Admin).
    /// </summary>
    [HttpDelete("system/purge-cache")]
    public IActionResult PurgeSystemCache()
    {
        var role = Request.Headers["X-User-Role"].FirstOrDefault() ?? "Guest";

        if (role != "Admin")
        {
            return StatusCode(403, new
            {
                Error = "403 Forbidden: Потрібні права системного адміністратора (Admin).",
                YourRole = role,
                Warning = "Спроба несанкціонованого доступу до адміністративного ендпоінту зафіксована в журналі безпеки!"
            });
        }

        return Ok(new
        {
            Message = "Адміністративну операцію 'purge-cache' успішно виконано.",
            ExecutedBy = "Admin",
            Timestamp = DateTime.UtcNow
        });
    }
}
