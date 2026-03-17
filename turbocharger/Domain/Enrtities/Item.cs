namespace Turbocharger.Domain.Entities;

public class Item
{
    public int item_id { get; set; }           // соответствует item_id в БД
    public string item_name { get; set; } = null!; // соответствует item_name в БД

    // Навигационные свойства
    public ICollection<Bom> ParentBoms { get; set; } = new List<Bom>();
    public ICollection<Bom> ComponentBoms { get; set; } = new List<Bom>();
}