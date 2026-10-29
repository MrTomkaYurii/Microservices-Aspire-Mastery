using Catalog.Api.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Catalog.Api.Controllers;

/// <summary>
/// Контролер каталогу товарів інтернет-магазину.
/// Демонструє взаємодію з базою даних PostgreSQL через Entity Framework Core в середовищі .NET Aspire.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class ProductsController : ControllerBase
{
    private readonly CatalogDbContext _db;
    private readonly ILogger<ProductsController> _logger;

    public ProductsController(CatalogDbContext db, ILogger<ProductsController> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Отримати повний перелік усіх товарів з каталогу.
    /// </summary>
    /// <returns>Список товарів.</returns>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<Product>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<Product>>> GetAll(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Отримання повного списку товарів з бази PostgreSQL...");
        var products = await _db.Products.AsNoTracking().ToListAsync(cancellationToken);
        return Ok(products);
    }

    /// <summary>
    /// Отримати деталі конкретного товару за його ідентифікатором.
    /// </summary>
    /// <param name="id">Унікальний ідентифікатор товару (GUID).</param>
    /// <param name="cancellationToken">Токен скасування операції.</param>
    /// <returns>Дані товару або 404 Not Found.</returns>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(Product), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<Product>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var product = await _db.Products.FindAsync([id], cancellationToken);
        if (product is null)
        {
            _logger.LogWarning("Товар з ID '{ProductId}' не знайдено в базі даних.", id);
            return NotFound(new { Message = $"Товар з ID '{id}' не знайдено." });
        }

        return Ok(product);
    }

    /// <summary>
    /// Створити та зберегти новий товар у базі даних PostgreSQL.
    /// </summary>
    /// <param name="product">Дані нового товару.</param>
    /// <param name="cancellationToken">Токен скасування.</param>
    /// <returns>Створений товар із присвоєним новим ID.</returns>
    [HttpPost]
    [ProducesResponseType(typeof(Product), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<Product>> Create([FromBody] Product product, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(product.Name))
        {
            return BadRequest(new { Message = "Назва товару не може бути порожньою." });
        }

        product.Id = Guid.NewGuid();
        product.CreatedAtUtc = DateTime.UtcNow;

        _db.Products.Add(product);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Створено новий товар: {ProductName} (ID: {ProductId})", product.Name, product.Id);

        return CreatedAtAction(nameof(GetById), new { id = product.Id }, product);
    }
}
