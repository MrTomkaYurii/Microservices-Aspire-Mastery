namespace Order.Client.Api.Models;

public record ReserveItemRequest(string ProductId, int Quantity);
public record StreamStockRequest(List<string> ProductIds);
