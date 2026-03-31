namespace Turbocharger.Domain.Entities;

public class StockBatch
{
    public int StockBatchId { get; set; }
    public int ItemId { get; set; }
    public int Quantity { get; set; }           
    public int InitialQuantity { get; set; }
    public decimal UnitPrice { get; set; }    
    public DateTime CreatedAt { get; set; }

    // Навигационные свойства
    public Item Item { get; set; } = null!;
}