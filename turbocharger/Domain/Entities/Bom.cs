namespace Turbocharger.Domain.Entities;

public class Bom
{
    public int BomId { get; set; }           
    public int? ParentId { get; set; }         
    public int ComponentId { get; set; }   
    public int Quantity { get; set; }        

    // Навигационные свойства
    public Item? Parent { get; set; }
    public Item Component { get; set; } = null!;
}