namespace Turbocharger.Domain.Entities;

public class Order
{
    public int OrderId { get; set; }
    public string Status { get; set; } = "Draft"; // Draft, Confirmed, Shipped, Cancelled
    public string? Comment { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);
    public DateTime OrderDate { get; set; } = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);
    public DateTime DueDate { get; set; } = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);

    public ICollection<OrderLine> Lines { get; set; } = new List<OrderLine>();
}

public class OrderLine
{
    public int OrderLineId { get; set; }
    public int OrderId { get; set; }
    public int ItemId { get; set; }
    public int Quantity { get; set; }

    public Order Order { get; set; } = null!;
    public Item Item { get; set; } = null!;
}
