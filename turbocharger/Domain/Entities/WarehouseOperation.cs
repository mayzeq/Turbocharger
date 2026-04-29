namespace Turbocharger.Domain.Entities;

public class WarehouseOperation
{
    public int OperationId { get; set; }
    public int ItemId { get; set; }
    public string OperationType { get; set; } = "Income"; // Income, Expense
    public int Quantity { get; set; }
    public string? Comment { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);
    public DateTime OperationDate { get; set; } = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);

    // Навигационные свойства
    public Item Item { get; set; } = null!;
}