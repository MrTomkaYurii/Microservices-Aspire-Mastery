using Inventory.Grpc.Services;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Grpc.Controllers;

/// <summary>
/// Діагностичний контролер gRPC-сервера складу, щоб при відкритті сервісу у браузері
/// відображався інтерактивний Swagger UI зі станом складу та специфікацією Protobuf-контракту.
/// </summary>
[ApiController]
[Route("api/grpc-diagnostics")]
[Produces("application/json")]
public class GrpcDiagnosticsController : ControllerBase
{
    /// <summary>
    /// Переглянути поточний стан залишків у пам'яті gRPC-сервера.
    /// </summary>
    [HttpGet("stock-snapshot")]
    public IActionResult GetStockSnapshot()
    {
        var items = InventoryGrpcServiceImpl.Storage.Select(kvp => new
        {
            ProductId = kvp.Key,
            ProductName = kvp.Value.Name,
            AvailableQuantity = kvp.Value.Stock,
            UnitPrice = kvp.Value.Price,
            InStock = kvp.Value.Stock > 0
        });

        return Ok(new
        {
            Service = "Inventory.Grpc Server (.NET 10 HTTP/2)",
            Protocol = "gRPC (Protobuf v3 over HTTP/2) + Diagnostic REST UI",
            Items = items
        });
    }

    /// <summary>
    /// Переглянути метадані активного Protobuf-контракту (inventory.proto).
    /// </summary>
    [HttpGet("proto-contract")]
    public IActionResult GetProtoContractInfo()
    {
        return Ok(new
        {
            Syntax = "proto3",
            Package = "inventory",
            GrpcService = "InventoryService",
            RpcMethods = new[]
            {
                new { Method = "GetProductStock", Type = "Unary RPC", Description = "Миттєва перевірка залишку та ціни за ProductId" },
                new { Method = "ReserveStock", Type = "Unary RPC", Description = "Атомарне зменшення залишку на складі" },
                new { Method = "GetStockStream", Type = "Server Streaming RPC", Description = "Неперервний потік статусів товарів через HTTP/2" }
            },
            HowToCall = "Використовуйте Swagger сервісу 'order-client-api' для виконання реальних бінарних gRPC-запитів через Aspire Service Discovery ('https+http://inventory-grpc')."
        });
    }
}
