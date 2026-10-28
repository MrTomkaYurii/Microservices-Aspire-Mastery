using System.Collections.Concurrent;
using Microsoft.AspNetCore.Mvc;

namespace Sample.Api.Controllers;

/// <summary>
/// 🧪 КРОК 1: ТЕСТОВИЙ API-ЕНДПОІНТ ДЛЯ ПЕРЕВІРКИ РОЗПОДІЛЕНИМИ ТЕСТАМИ
/// 
/// 💡 НАВІЩО ПОТРІБЕН ЦЕЙ КОНТРОЛЕР?
/// Цей контролер надає повний набір CRUD-операцій (Create, Read, Delete) над елементами.
/// Проєкт 'Tests.ClusterIntegration' запускає повноцінний екземпляр Aspire AppHost 
/// та тестує ці методи через справжній HTTP-клієнт із резолвом імен (Service Discovery).
/// </summary>
[ApiController]
[Route("api/step1-items")]
public class Step1_ItemsController : ControllerBase
{
    private static readonly ConcurrentDictionary<Guid, ItemDto> Items = new();

    static Step1_ItemsController()
    {
        // Початкові тестові дані
        var id1 = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var id2 = Guid.Parse("22222222-2222-2222-2222-222222222222");
        Items[id1] = new ItemDto(id1, "Aspire Microservice Book", 49.99m);
        Items[id2] = new ItemDto(id2, "Distributed Systems Course", 199.00m);
    }

    /// <summary>
    /// Отримати всі елементи каталогу.
    /// </summary>
    [HttpGet]
    public IActionResult GetAll()
    {
        return Ok(Items.Values.ToList());
    }

    /// <summary>
    /// Отримати елемент за унікальним ідентифікатором.
    /// </summary>
    [HttpGet("{id:guid}")]
    public IActionResult GetById(Guid id)
    {
        if (Items.TryGetValue(id, out var item))
        {
            return Ok(item);
        }

        return NotFound(new { Error = $"Елемент з Id '{id}' не знайдено." });
    }

    /// <summary>
    /// Створити новий елемент.
    /// </summary>
    [HttpPost]
    public IActionResult Create([FromBody] CreateItemRequest request)
    {
        var id = Guid.NewGuid();
        var newItem = new ItemDto(id, request.Name, request.Price);
        Items[id] = newItem;

        return CreatedAtAction(nameof(GetById), new { id }, newItem);
    }

    /// <summary>
    /// Видалити елемент за ідентифікатором.
    /// </summary>
    [HttpDelete("{id:guid}")]
    public IActionResult Delete(Guid id)
    {
        if (Items.TryRemove(id, out _))
        {
            return NoContent();
        }

        return NotFound();
    }
}

public record ItemDto(Guid Id, string Name, decimal Price);
public record CreateItemRequest(string Name, decimal Price);
