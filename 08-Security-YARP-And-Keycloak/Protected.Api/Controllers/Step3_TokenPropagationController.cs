using Microsoft.AspNetCore.Mvc;

namespace Protected.Api.Controllers;

/// <summary>
/// 🔑 КРОК 3: ПАТЕРН ТРАНСЛЯЦІЇ ОСОБИ (TOKEN & CLAIM PROPAGATION)
/// 
/// 💡 НАВІЩО ПОТРІБЕН TOKEN PROPAGATION?
/// У мікросервісній архітектурі існує дві стратегії безпеки:
/// 
/// 1. Важка валідація на кожному кроці (Zero-Trust Cryptography):
///    Кожен із 10 внутрішніх мікросервісів знову й знову розшифровує RSA-підпис JWT токена.
///    Мінус: високе навантаження на процесор та затримка відповіді (Latency).
/// 
/// 2. Патерн Gateway Offloading & Header Propagation (Рекомендований):
///    YARP Gateway перевіряє криптографічний JWT-токен ОДИН РАЗ на вході.
///    Якщо токен валідний, Gateway підставляє перевірені внутрішні заголовки:
///      'X-User-Id': 'usr_98124'
///      'X-User-Email': 'dev@mastery.com'
///      'X-User-Role': 'Buyer'
///    Внутрішній захищений контур довіряє цим заголовкам, оскільки зовнішній світ 
///    не має прямого доступу до внутрішніх портів сервісів.
/// </summary>
[ApiController]
[Route("api/step3-propagation")]
public class Step3_TokenPropagationController : ControllerBase
{
    /// <summary>
    /// Отримати профіль поточного користувача через прокинуті заголовки ідентичності.
    /// </summary>
    [HttpGet("user-profile")]
    public IActionResult GetUserProfile()
    {
        // Читаємо заголовки ідентичності, прокинуті через YARP Gateway
        var userId = Request.Headers["X-User-Id"].FirstOrDefault() ?? User.FindFirst("sub")?.Value ?? "guest_anonymous";
        var userEmail = Request.Headers["X-User-Email"].FirstOrDefault() ?? User.FindFirst("email")?.Value ?? "guest@example.com";
        var userRole = Request.Headers["X-User-Role"].FirstOrDefault() ?? User.FindFirst("role")?.Value ?? "Guest";
        var correlationId = Request.Headers["X-Correlation-Id"].FirstOrDefault() ?? Guid.NewGuid().ToString("N");

        var isAuthenticated = userId != "guest_anonymous";

        return Ok(new
        {
            Preamble = "Дані користувача успішно витягнуто через патерн Token Propagation.",
            IsAuthenticated = isAuthenticated,
            SecurityContext = new
            {
                UserId = userId,
                Email = userEmail,
                Role = userRole,
                TraceCorrelationId = correlationId
            },
            Explanation = isAuthenticated
                ? "Запит пройшов крізь YARP Gateway, де JWT було перевірено та трансльовано у внутрішній контекст."
                : "Запит виконано без токена авторизації (анонімний гість)."
        });
    }
}
