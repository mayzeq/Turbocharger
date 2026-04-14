using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;
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
            .Select(ToDto())
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
                CurrentQuantity = i.CurrentQuantity,
                ReservedQuantity = i.ReservedQuantity,
                AvailableQuantity = i.CurrentQuantity - i.ReservedQuantity
            })
            .ToListAsync();

        return Ok(sellableItems);
    }

    [HttpGet("mrp-shortages")]
    public async Task<ActionResult<IEnumerable<MrpShortageDto>>> GetMrpShortages()
    {
        var activeOrders = await _context.Orders
            .Where(o => o.Status == "Draft" || o.Status == "Confirmed")
            .Select(o => new { o.ItemId, o.Quantity })
            .ToListAsync();

        var bomRows = await _context.BOM
            .Select(b => new { b.ParentId, b.ComponentId, b.Quantity })
            .ToListAsync();

        var items = await _context.Item
            .Select(i => new { i.ItemId, i.ItemName, i.CurrentQuantity, i.ReservedQuantity })
            .ToListAsync();

        var childrenByParent = bomRows
            .Where(b => b.ParentId.HasValue)
            .GroupBy(b => b.ParentId!.Value)
            .ToDictionary(g => g.Key, g => g.Select(x => (x.ComponentId, x.Quantity)).ToList());

        var requirements = new Dictionary<int, int>();
        foreach (var order in activeOrders)
        {
            AddLeafRequirements(order.ItemId, order.Quantity, childrenByParent, requirements);
        }

        var itemMap = items.ToDictionary(i => i.ItemId, i => i);
        var shortages = requirements
            .Select(r =>
            {
                if (!itemMap.TryGetValue(r.Key, out var item))
                    return null;

                var available = item.CurrentQuantity - item.ReservedQuantity;
                var shortage = Math.Max(0, r.Value - available);
                if (shortage <= 0)
                    return null;

                return new MrpShortageDto
                {
                    ItemId = item.ItemId,
                    ItemName = item.ItemName,
                    RequiredQuantity = r.Value,
                    AvailableQuantity = available,
                    ShortageQuantity = shortage
                };
            })
            .Where(x => x != null)
            .OrderByDescending(x => x!.ShortageQuantity)
            .ThenBy(x => x!.ItemId)
            .Select(x => x!)
            .ToList();

        return Ok(shortages);
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

        var order = new Order
        {
            ItemId = dto.ItemId,
            Quantity = dto.Quantity,
            UnitPrice = dto.UnitPrice,
            TotalAmount = dto.Quantity * dto.UnitPrice,
            Status = "Draft",
            Comment = dto.Comment,
            CreatedAt = DateTime.UtcNow,
            OrderDate = DateTime.SpecifyKind(dto.OrderDate, DateTimeKind.Utc)
        };

        _context.Orders.Add(order);
        await _context.SaveChangesAsync();
        await _context.Entry(order).Reference(o => o.Item).LoadAsync();

        return Ok(new OrderDto
        {
            OrderId = order.OrderId,
            ItemId = order.ItemId,
            ItemName = order.Item.ItemName,
            Quantity = order.Quantity,
            UnitPrice = order.UnitPrice,
            TotalAmount = order.TotalAmount,
            Status = order.Status,
            Comment = order.Comment,
            CreatedAt = order.CreatedAt,
            OrderDate = order.OrderDate
        });
    }

    [HttpPost("{orderId}/confirm")]
    public async Task<ActionResult<OrderDto>> ConfirmOrder(int orderId)
    {
        var order = await _context.Orders.Include(o => o.Item).FirstOrDefaultAsync(o => o.OrderId == orderId);
        if (order == null)
            return NotFound("Заказ не найден.");
        if (order.Status != "Draft")
            return BadRequest("Подтверждать можно только заказ в статусе Черновик.");

        var available = order.Item.CurrentQuantity - order.Item.ReservedQuantity;
        if (available < order.Quantity)
            return BadRequest($"Недостаточно доступного остатка. Доступно: {available}, требуется: {order.Quantity}.");

        order.Item.ReservedQuantity += order.Quantity;
        order.Status = "Confirmed";
        await _context.SaveChangesAsync();

        return Ok(MapOrder(order));
    }

    [HttpPost("{orderId}/ship")]
    public async Task<ActionResult<OrderDto>> ShipOrder(int orderId)
    {
        var order = await _context.Orders.Include(o => o.Item).FirstOrDefaultAsync(o => o.OrderId == orderId);
        if (order == null)
            return NotFound("Заказ не найден.");
        if (order.Status != "Confirmed")
            return BadRequest("Отгружать можно только подтвержденный заказ.");

        if (order.Item.ReservedQuantity < order.Quantity || order.Item.CurrentQuantity < order.Quantity)
            return BadRequest("Неконсистентные остатки/резервы. Проверьте складские данные.");

        await using var tx = await _context.Database.BeginTransactionAsync();
        try
        {
            order.Item.ReservedQuantity -= order.Quantity;
            order.Item.CurrentQuantity -= order.Quantity;
            order.Status = "Shipped";

            _context.WarehouseOperations.Add(new WarehouseOperation
            {
                ItemId = order.ItemId,
                OperationType = "Expense",
                Quantity = order.Quantity,
                UnitPrice = order.UnitPrice,
                Comment = string.IsNullOrWhiteSpace(order.Comment)
                    ? $"Отгрузка по заказу #{order.OrderId}"
                    : $"Отгрузка по заказу #{order.OrderId}. {order.Comment}",
                CreatedAt = DateTime.UtcNow,
                OperationDate = order.OrderDate
            });

            await _context.SaveChangesAsync();
            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }

        return Ok(MapOrder(order));
    }

    [HttpPost("{orderId}/cancel")]
    public async Task<ActionResult<OrderDto>> CancelOrder(int orderId)
    {
        var order = await _context.Orders.Include(o => o.Item).FirstOrDefaultAsync(o => o.OrderId == orderId);
        if (order == null)
            return NotFound("Заказ не найден.");
        if (order.Status == "Shipped")
            return BadRequest("Отгруженный заказ нельзя отменить.");
        if (order.Status == "Cancelled")
            return BadRequest("Заказ уже отменен.");

        if (order.Status == "Confirmed")
        {
            if (order.Item.ReservedQuantity < order.Quantity)
                return BadRequest("Неконсистентные резервы. Невозможно отменить заказ.");

            order.Item.ReservedQuantity -= order.Quantity;
        }

        order.Status = "Cancelled";
        await _context.SaveChangesAsync();

        return Ok(MapOrder(order));
    }

    private static Expression<Func<Order, OrderDto>> ToDto()
    {
        return o => new OrderDto
        {
            OrderId = o.OrderId,
            ItemId = o.ItemId,
            ItemName = o.Item.ItemName,
            Quantity = o.Quantity,
            UnitPrice = o.UnitPrice,
            TotalAmount = o.TotalAmount,
            Status = o.Status,
            Comment = o.Comment,
            CreatedAt = o.CreatedAt,
            OrderDate = o.OrderDate
        };
    }

    private static OrderDto MapOrder(Order order)
    {
        return new OrderDto
        {
            OrderId = order.OrderId,
            ItemId = order.ItemId,
            ItemName = order.Item.ItemName,
            Quantity = order.Quantity,
            UnitPrice = order.UnitPrice,
            TotalAmount = order.TotalAmount,
            Status = order.Status,
            Comment = order.Comment,
            CreatedAt = order.CreatedAt,
            OrderDate = order.OrderDate
        };
    }

    private static void AddLeafRequirements(
        int itemId,
        int multiplier,
        IReadOnlyDictionary<int, List<(int ComponentId, int Quantity)>> childrenByParent,
        IDictionary<int, int> requirements)
    {
        if (!childrenByParent.TryGetValue(itemId, out var children) || children.Count == 0)
        {
            requirements[itemId] = requirements.TryGetValue(itemId, out var current)
                ? current + multiplier
                : multiplier;
            return;
        }

        foreach (var child in children)
        {
            AddLeafRequirements(child.ComponentId, multiplier * child.Quantity, childrenByParent, requirements);
        }
    }
}
