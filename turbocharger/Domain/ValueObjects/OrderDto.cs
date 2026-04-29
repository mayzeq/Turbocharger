namespace Turbocharger.Domain.ValueObjects;

public class OrderDto
{
    public int OrderId { get; set; }
    public string Status { get; set; } = null!;
    public string? Comment { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime OrderDate { get; set; }
    public DateTime DueDate { get; set; }
    public List<OrderLineDto> Lines { get; set; } = new();
}

public class OrderCreateDto
{
    public List<OrderLineCreateDto> Lines { get; set; } = new();
    public string? Comment { get; set; }
    public DateTime OrderDate { get; set; }
    public DateTime DueDate { get; set; }
}

public class OrderLineDto
{
    public int OrderLineId { get; set; }
    public int ItemId { get; set; }
    public string ItemName { get; set; } = null!;
    public int Quantity { get; set; }
}

public class OrderLineCreateDto
{
    public int ItemId { get; set; }
    public int Quantity { get; set; }
}

public class SellableItemDto
{
    public int ItemId { get; set; }
    public string ItemName { get; set; } = null!;
    public int CurrentQuantity { get; set; }
    public int ReservedQuantity { get; set; }
    public int AvailableQuantity { get; set; }
}

public class MrpShortageDto
{
    public int ItemId { get; set; }
    public string ItemName { get; set; } = null!;
    public int RequiredQuantity { get; set; }
    public int AvailableQuantity { get; set; }
    public int ShortageQuantity { get; set; }
}
