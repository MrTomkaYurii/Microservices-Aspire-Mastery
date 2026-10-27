using Microsoft.AspNetCore.Mvc;

namespace YarpGateway.Controllers;

/// <summary>
/// 🌐 КРОК 1: ШЛЮЗ YARP ТА МАРШРУТИЗАЦІЯ (REVERSE PROXY FUNDAMENTALS)
/// 
/// 💡 НАВІЩО ПОТРІБЕН API GATEWAY (YARP)?
/// 1. Єдина точка входу (Single Entry Point):
///    Клієнтський мобільний додаток або браузер знає лише ОДНУ адресу шлюзу (https://api.shop.com).
///    Йому не потрібно знати внутрішні адреси 15 мікросервісів.
/// 2. Приховування внутрішньої топології:
///    Всі мікросервіси знаходяться в ізольованій віртуальній мережі без доступу з інтернету.
/// 3. Маршрутизація та трансформація:
///    Шлюз приймає /proxy/public/catalog і прозоро перенаправляє на http://protected-api/api/step2-public/catalog.
/// </summary>
[ApiController]
[Route("api/step1-gateway")]
public class Step1_GatewayStatusController : ControllerBase
{
    private readonly IConfiguration _configuration;

    public Step1_GatewayStatusController(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    /// <summary>
    /// Отримати зведену інформацію про активні маршрути YARP та кластери призначення.
    /// </summary>
    [HttpGet("routes")]
    public IActionResult GetGatewayRoutes()
    {
        return Ok(new
        {
            Preamble = "YARP (Yet Another Reverse Proxy) інтегровано з .NET Aspire Service Discovery.",
            GatewayRole = "Зовнішній фасад системи, що розподіляє трафік та транслює заголовки безпеки.",
            ActiveRoutes = new[]
            {
                new
                {
                    Route = "/proxy/public/{**catch-all}",
                    Cluster = "protected-cluster",
                    Destination = "https+http://protected-api",
                    Policy = "Anonymous / RateLimited",
                    Transforms = "RemovePrefix: /proxy/public"
                },
                new
                {
                    Route = "/proxy/secure/{**catch-all}",
                    Cluster = "protected-cluster",
                    Destination = "https+http://protected-api",
                    Policy = "TokenValidation & Propagation",
                    Transforms = "RemovePrefix: /proxy/secure"
                }
            },
            KeycloakContainer = new
            {
                Url = "http://localhost:8080",
                AdminConsole = "http://localhost:8080/admin",
                Credentials = "admin / admin",
                Role = "Центральний сервер авторизації (OAuth2 / OIDC Realm)"
            }
        });
    }
}
