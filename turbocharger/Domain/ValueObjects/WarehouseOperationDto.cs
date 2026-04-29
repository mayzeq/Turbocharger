namespace Turbocharger.ValueObjects;

public class WarehouseOperationDto
{
    public int OperationId { get; set; }
    public int ItemId { get; set; }
    public string ItemName { get; set; } = null!;
    public string OperationType { get; set; } = null!;
    public int Quantity { get; set; }
    public string? Comment { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime OperationDate { get; set; }
}

public class WarehouseOperationCreateDto
{
    public int ItemId { get; set; }
    public string OperationType { get; set; } = null!;
    public int Quantity { get; set; }
    public string? Comment { get; set; }
    public DateTime OperationDate { get; set; }
}