namespace Client.Web;

public record ProductItem(
    Guid Id,
    string Name,
    string Category,
    string Description,
    decimal Price,
    int StockQuantity,
    DateTime CreatedAtUtc);

/// <summary>
/// HTTP-клієнт для взаємодії з мікросервісом Catalog.Api.
/// Використовує Aspire Service Discovery через базову адресу "https+http://catalog-api".
/// </summary>
public class CatalogApiClient(HttpClient httpClient)
{
    public async Task<ProductItem[]> GetProductsAsync(CancellationToken cancellationToken = default)
    {
        return await httpClient.GetFromJsonAsync<ProductItem[]>("/api/products", cancellationToken) ?? [];
    }
}
