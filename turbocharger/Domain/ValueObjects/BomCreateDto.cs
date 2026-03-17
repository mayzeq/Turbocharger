namespace Turbocharger.Domain.ValueObjects;

public class BomCreateDto
{
    public int? ParentId { get; set; }
    public int ComponentId { get; set; }
    public int Quantity { get; set; }
}