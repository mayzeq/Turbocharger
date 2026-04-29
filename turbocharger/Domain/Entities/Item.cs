namespace Turbocharger.Domain.Entities;

public class Item
{
    public int ItemId { get; set; }
    public string ItemName { get; set; } = null!;

    // Навигационные свойства
    public ICollection<Bom> ParentBoms { get; set; } = new List<Bom>();
    public ICollection<Bom> ComponentBoms { get; set; } = new List<Bom>();
    public ICollection<WarehouseOperation> WarehouseOperations { get; set; } = new List<WarehouseOperation>();
    public ICollection<OrderLine> OrderLines { get; set; } = new List<OrderLine>();
}