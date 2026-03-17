namespace Turbocharger.Domain.ValueObjects;

public class BomResponseDto
{
    public int BomId { get; set; }
    public int? ParentId { get; set; }
    public string? ParentName { get; set; }
    public int ComponentId { get; set; }
    public string ComponentName { get; set; } = null!;
    public int Quantity { get; set; }
}