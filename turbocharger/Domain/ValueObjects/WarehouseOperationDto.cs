namespace Turbocharger.ValueObjects;

public class WarehouseOperationDto
{
    public int OperationId { get; set; }
    public int ItemId { get; set; }
    public string ItemName { get; set; } = null!;
    public string OperationType { get; set; } = null!;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public string? Comment { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class WarehouseOperationCreateDto
{
    public int ItemId { get; set; }
    public string OperationType { get; set; } = null!;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public string? Comment { get; set; }
}

public class StockBatchDto
{
    public int StockBatchId { get; set; }
    public int ItemId { get; set; }
    public string ItemName { get; set; } = null!;
    public int Quantity { get; set; }
    public int InitialQuantity { get; set; }
    public decimal UnitPrice { get; set; }
    public DateTime CreatedAt { get; set; }
}