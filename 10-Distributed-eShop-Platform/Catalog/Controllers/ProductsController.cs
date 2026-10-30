using Catalog.Models;
using Catalog.Services;
using Microsoft.AspNetCore.Mvc;

namespace Catalog.Controllers;

/// <summary>
/// Контролер каталогу товарів eShop (PostgreSQL + EF Core).
/// </summary>
[ApiController]
[Route("api/products")]
[Produces("application/json")]
public class ProductsController : ControllerBase
{
    private readonly ProductService _service;

    public ProductsController(ProductService service)
    {
        _service = service;
    }

    /// <summary>
    /// Отримати всі товари каталогу.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(List<Product>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll()
    {
        var products = await _service.GetProductsAsync();
        return Ok(products);
    }

    /// <summary>
    /// Отримати товар за унікальним Id.
    /// </summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(Product), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(int id)
    {
        var product = await _service.GetProductByIdAsync(id);
        if (product is null) return NotFound();

        return Ok(product);
    }

    /// <summary>
    /// Створити новий товар.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(Product), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] Product product)
    {
        await _service.CreateProductAsync(product);
        return CreatedAtAction(nameof(GetById), new { id = product.Id }, product);
    }

    /// <summary>
    /// Оновити існуючий товар.
    /// </summary>
    [HttpPut("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(int id, [FromBody] Product inputProduct)
    {
        var updatedProduct = await _service.GetProductByIdAsync(id);
        if (updatedProduct is null) return NotFound();

        await _service.UpdateProductAsync(updatedProduct, inputProduct);
        return NoContent();
    }

    /// <summary>
    /// Видалити товар за Id.
    /// </summary>
    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(int id)
    {
        var deletedProduct = await _service.GetProductByIdAsync(id);
        if (deletedProduct is null) return NotFound();

        await _service.DeleteProductAsync(deletedProduct);
        return NoContent();
    }

    /// <summary>
    /// Пошук товарів за запитом.
    /// </summary>
    [HttpGet("search/{query}")]
    [ProducesResponseType(typeof(List<Product>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Search(string query)
    {
        var products = await _service.SearchProductsAsync(query);
        return Ok(products);
    }
}
