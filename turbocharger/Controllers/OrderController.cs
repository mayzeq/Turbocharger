using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Turbocharger.Domain.Entities;
using Turbocharger.Domain.ValueObjects;
using Turbocharger.Storage;

namespace Turbocharger.Controllers;

[Route("api/[controller]")]
[ApiController]
public class OrderController : ControllerBase
{
    private readonly AppDbContext _context;

    public OrderController(AppDbContext context) => _context = context;

    [HttpGet]
    public async Task<ActionResult<IEnumerable<OrderDto>>> GetOrders()
    {
        var orders = await _context.Orders
            .Include(o => o.Item)
            .OrderByDescending(o => o.OrderDate)
            .ThenByDescending(o => o.OrderId)
            .Select(o => new OrderDto
            {
                OrderId = o.OrderId,
                ItemId = o.ItemId,
                ItemName = o.Item.ItemName,
                Quantity = o.Quantity,
                UnitPrice = o.UnitPrice,
                TotalAmount = o.TotalAmount,
                Comment = o.Comment,
                CreatedAt = o.CreatedAt,
                OrderDate = o.OrderDate
            })
            .ToListAsync();

        return Ok(orders);
    }

    [HttpGet("sellable-items")]
    public async Task<ActionResult<IEnumerable<SellableItemDto>>> GetSellableItems()
    {
        var sellableItems = await _context.Item
            .Where(i => _context.BOM.Any(b => b.ParentId == i.ItemId))
            .OrderBy(i => i.ItemId)
            .Select(i => new SellableItemDto
            {
                ItemId = i.ItemId,
                ItemName = i.ItemName,
                CurrentQuantity = i.CurrentQuantity
            })
            .ToListAsync();

        return Ok(sellableItems);
    }

    [HttpPost]
    public async Task<ActionResult<OrderDto>> CreateOrder([FromBody] OrderCreateDto dto)
    {
        if (dto.Quantity <= 0)
            return BadRequest("Количество в заказе должно быть больше 0.");

        if (dto.UnitPrice < 0)
            return BadRequest("Цена продажи не может быть отрицательной.");

        var item = await _context.Item.FindAsync(dto.ItemId);
        if (item == null)
            return BadRequest("Элемент не найден.");

        var hasChildren = await _context.BOM.AnyAsync(b => b.ParentId == dto.ItemId);
        if (!hasChildren)
            return BadRequest("Продажа запрещена: нельзя продавать детали нижнего уровня.");

        if (item.CurrentQuantity < dto.Quantity)
            return BadRequest($"Недостаточно на складе. Доступно: {item.CurrentQuantity}, требуется: {dto.Quantity}.");

        var orderDateUtc = DateTime.SpecifyKind(dto.OrderDate, DateTimeKind.Utc);

        await using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            var order = new Order
            {
                ItemId = dto.ItemId,
                Quantity = dto.Quantity,
                UnitPrice = dto.UnitPrice,
                TotalAmount = dto.Quantity * dto.UnitPrice,
                Comment = dto.Comment,
                CreatedAt = DateTime.UtcNow,
                OrderDate = orderDateUtc
            };

            _context.Orders.Add(order);

            item.CurrentQuantity -= dto.Quantity;

            _context.WarehouseOperations.Add(new WarehouseOperation
            {
                ItemId = dto.ItemId,
                OperationType = "Expense",
                Quantity = dto.Quantity,
                UnitPrice = dto.UnitPrice,
                Comment = string.IsNullOrWhiteSpace(dto.Comment)
                    ? "Продажа по заказу"
                    : $"Продажа по заказу. {dto.Comment}",
                CreatedAt = DateTime.UtcNow,
                OperationDate = orderDateUtc
            });

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            return Ok(new OrderDto
            {
                OrderId = order.OrderId,
                ItemId = item.ItemId,
                ItemName = item.ItemName,
                Quantity = order.Quantity,
                UnitPrice = order.UnitPrice,
                TotalAmount = order.TotalAmount,
                Comment = order.Comment,
                CreatedAt = order.CreatedAt,
                OrderDate = order.OrderDate
            });
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
}
