namespace Turbocharger.Domain.Entities;

public class Item
{
    public int ItemId { get; set; }
    public string ItemName { get; set; } = null!;

    // Складские поля
    public int CurrentQuantity { get; set; }    
    public decimal PurchasePrice { get; set; }   

    // Навигационные свойства
    public ICollection<Bom> ParentBoms { get; set; } = new List<Bom>();
    public ICollection<Bom> ComponentBoms { get; set; } = new List<Bom>();
    public ICollection<WarehouseOperation> WarehouseOperations { get; set; } = new List<WarehouseOperation>();
    public ICollection<StockBatch> StockBatches { get; set; } = new List<StockBatch>();
}