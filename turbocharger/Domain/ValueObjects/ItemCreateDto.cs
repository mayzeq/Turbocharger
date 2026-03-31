namespace Turbocharger.Domain.ValueObjects;

public class ItemCreateDto
{
    public string ItemName { get; set; } = null!;
    public decimal PurchasePrice { get; set; } = 0;
}