namespace Turbocharger.Domain.ValueObjects;

public class ItemResponseDto
{
    public int ItemId { get; set; }
    public string ItemName { get; set; } = null!;
    public int CurrentQuantity { get; set; }
    public decimal PurchasePrice { get; set; }

}