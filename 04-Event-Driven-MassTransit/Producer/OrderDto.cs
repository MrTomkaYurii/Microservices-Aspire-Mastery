namespace Producer
{
    public class OrderDto
    {
        public required string ProductName { get; set; }

        public decimal Price { get; set; }

        public int Quantity { get; set; }
    }
}
