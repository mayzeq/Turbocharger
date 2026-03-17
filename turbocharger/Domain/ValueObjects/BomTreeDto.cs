namespace Turbocharger.Domain.ValueObjects;

public class BomTreeDto
{
    public int ItemId { get; set; }
    public string ItemName { get; set; } = null!;
    public int Level { get; set; }
    public int TotalQuantity { get; set; }
    public List<BomTreeDto> Children { get; set; } = new();
}