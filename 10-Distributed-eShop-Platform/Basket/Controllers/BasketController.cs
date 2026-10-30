using Basket.Models;
using Basket.Services;
using Microsoft.AspNetCore.Mvc;

namespace Basket.Controllers;

/// <summary>
/// Контролер кошика покупця eShop (Redis HybridCache + PostgreSQL).
/// </summary>
[ApiController]
[Route("api/basket")]
[Produces("application/json")]
public class BasketController : ControllerBase
{
    private readonly BasketService _service;

    public BasketController(BasketService service)
    {
        _service = service;
    }

    /// <summary>
    /// Отримати кошик за іменем користувача.
    /// </summary>
    [HttpGet("{userName}")]
    [ProducesResponseType(typeof(ShoppingCart), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetBasket(string userName)
    {
        var shoppingCart = await _service.GetBasket(userName);
        if (shoppingCart is null)
        {
            return NotFound(new { Message = $"Кошик для користувача '{userName}' не знайдено." });
        }

        return Ok(shoppingCart);
    }

    /// <summary>
    /// Оновити або зберегти кошик.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(ShoppingCart), StatusCodes.Status201Created)]
    public async Task<IActionResult> UpdateBasket([FromBody] ShoppingCart shoppingCart)
    {
        await _service.UpdateBasket(shoppingCart);
        return CreatedAtAction(nameof(GetBasket), new { userName = shoppingCart.UserName }, shoppingCart);
    }

    /// <summary>
    /// Оформити замовлення з кошика (Checkout).
    /// </summary>
    [HttpPost("checkout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Checkout([FromBody] BasketCheckout basketCheckout)
    {
        await _service.CheckoutBasket(basketCheckout);
        return NoContent();
    }

    /// <summary>
    /// Очистити/видалити кошик користувача.
    /// </summary>
    [HttpDelete("{userName}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteBasket(string userName)
    {
        await _service.DeleteBasket(userName);
        return NoContent();
    }
}
