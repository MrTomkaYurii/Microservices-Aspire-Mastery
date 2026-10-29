namespace Pricing.Api.Models;

public record UpdatePriceRequest(Guid ProductId, decimal NewPrice, string Reason);
public record ApplyDiscountRequest(string DiscountCode, decimal DiscountPercent);

public record ProductPriceDto(Guid ProductId, string ProductName, decimal CurrentPrice);
public record PricingOverviewResponse(decimal DiscountPercent, List<ProductPriceDto> Products);
