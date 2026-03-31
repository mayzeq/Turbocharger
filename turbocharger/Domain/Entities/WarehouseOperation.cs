namespace Turbocharger.Domain.Entities;

public class WarehouseOperation
{
    public int OperationId { get; set; }
    public int ItemId { get; set; }
    public string OperationType { get; set; } = "Income"; // Income, Expense, Adjustment
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public string? Comment { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Навигационные свойства
    public Item Item { get; set; } = null!;
}