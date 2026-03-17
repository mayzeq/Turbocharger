namespace Turbocharger.Domain.Entities;

public class Bom
{
    public int bom_id { get; set; }             // соответствует bom_id в БД
    public int? parent_id { get; set; }          // соответствует parent_id в БД
    public int component_id { get; set; }        // соответствует component_id в БД
    public int quantity { get; set; }            // соответствует quantity в БД

    // Навигационные свойства
    public Item? Parent { get; set; }
    public Item Component { get; set; } = null!;
}