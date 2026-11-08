using Microsoft.AspNetCore.Mvc;

namespace YarpApiGateway.Controllers;

/// <summary>
/// Контролер діагностики та маршрутизації шлюзу YARP для платформи eShop.
/// </summary>
[ApiController]
[Route("api/gateway")]
[Produces("application/json")]
public class GatewayStatusController : ControllerBase
{
    /// <summary>
    /// Отримати активну таблицю маршрутизації YARP Reverse Proxy та підключених кластерів eShop.
    /// </summary>
    [HttpGet("routes")]
    public IActionResult GetGatewayRoutes()
    {
        return Ok(new
        {
            Gateway = "eShop YARP API Gateway (.NET 10)",
            RateLimiting = "FixedWindowLimiter ('fixed': 5 requests per 10 seconds on ordering-route)",
            ConfiguredRoutes = new[]
            {
                new
                {
                    RouteId = "catalog-route",
                    IncomingPattern = "/catalog-service/{**catch-all}",
                    ClusterId = "catalog-cluster",
                    ServiceDiscoveryAddress = "https+http://catalog"
                },
                new
                {
                    RouteId = "basket-route",
                    IncomingPattern = "/basket-service/{**catch-all}",
                    ClusterId = "basket-cluster",
                    ServiceDiscoveryAddress = "https+http://basket"
                },
                new
                {
                    RouteId = "ordering-route",
                    IncomingPattern = "/ordering-service/{**catch-all}",
                    ClusterId = "ordering-cluster",
                    ServiceDiscoveryAddress = "https+http://ordering"
                }
            }
        });
    }
}
