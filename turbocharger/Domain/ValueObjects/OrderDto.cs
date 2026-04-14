namespace Turbocharger.Domain.ValueObjects;

public class OrderDto
{
    public int OrderId { get; set; }
    public int ItemId { get; set; }
    public string ItemName { get; set; } = null!;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal TotalAmount { get; set; }
    public string? Comment { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime OrderDate { get; set; }
}

public class OrderCreateDto
{
    public int ItemId { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public string? Comment { get; set; }
    public DateTime OrderDate { get; set; }
}

public class SellableItemDto
{
    public int ItemId { get; set; }
    public string ItemName { get; set; } = null!;
    public int CurrentQuantity { get; set; }
}
