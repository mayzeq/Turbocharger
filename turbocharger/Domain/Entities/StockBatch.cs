namespace Turbocharger.Domain.Entities;

public class StockBatch
{
    public int StockBatchId { get; set; }
    public int ItemId { get; set; }
    public int Quantity { get; set; }           // остаток в партии
    public int InitialQuantity { get; set; }    // исходное количество
    public decimal UnitPrice { get; set; }      // цена за единицу в этой партии
    public DateTime CreatedAt { get; set; }     // дата поступления (для FIFO)

    // Навигационные свойства
    public Item Item { get; set; } = null!;
}