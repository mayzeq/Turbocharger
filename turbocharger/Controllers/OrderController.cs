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
            .Include(o => o.Lines)
            .ThenInclude(l => l.Item)
            .OrderByDescending(o => o.OrderDate)
            .ThenByDescending(o => o.OrderId)
            .ToListAsync();

        return Ok(orders.Select(MapOrder));
    }

    [HttpGet("sellable-items")]
    public async Task<ActionResult<IEnumerable<SellableItemDto>>> GetSellableItems()
    {
        var balances = await BuildBalancesAsync();
        var sellableItemsRaw = await _context.Item
            .Where(i => _context.BOM.Any(b => b.ParentId == i.ItemId))
            .OrderBy(i => i.ItemId)
            .Select(i => new SellableItemDto
            {
                ItemId = i.ItemId,
                ItemName = i.ItemName
            })
            .ToListAsync();

        var sellableItems = sellableItemsRaw.Select(i =>
        {
            balances.TryGetValue(i.ItemId, out var balance);
            i.CurrentQuantity = balance?.CurrentQuantity ?? 0;
            i.ReservedQuantity = balance?.ReservedQuantity ?? 0;
            i.AvailableQuantity = balance?.AvailableQuantity ?? 0;
            return i;
        });

        return Ok(sellableItems);
    }

    [HttpGet("mrp-shortages")]
    public async Task<ActionResult<IEnumerable<MrpShortageDto>>> GetMrpShortages()
    {
        var activeOrders = await _context.OrderLines
            .Where(l => l.Order.Status == "Draft" || l.Order.Status == "Confirmed")
            .Select(l => new { l.ItemId, l.Quantity })
            .ToListAsync();

        var bomRows = await _context.BOM
            .Select(b => new { b.ParentId, b.ComponentId, b.Quantity })
            .ToListAsync();

        var items = await _context.Item.Select(i => new { i.ItemId, i.ItemName }).ToListAsync();
        var operationRows = await _context.WarehouseOperations
            .Select(o => new { o.ItemId, o.OperationType, o.Quantity })
            .ToListAsync();
        var currentStockByItem = operationRows
            .GroupBy(o => o.ItemId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.OperationType == "Income" ? x.Quantity : -x.Quantity));

        var childrenByParent = bomRows
            .Where(b => b.ParentId.HasValue)
            .GroupBy(b => b.ParentId!.Value)
            .ToDictionary(g => g.Key, g => g.Select(x => (x.ComponentId, x.Quantity)).ToList());

        // Остатки промежуточных уровней (узлов/сборок) уменьшают требование к более нижним уровням.
        var stockForNetting = new Dictionary<int, int>(currentStockByItem);
        var leafRequirements = new Dictionary<int, int>();

        foreach (var order in activeOrders)
        {
            AddLeafRequirementsWithUpperLevelStockNetting(
                order.ItemId,
                order.Quantity,
                childrenByParent,
                stockForNetting,
                leafRequirements);
        }

        var itemMap = items.ToDictionary(i => i.ItemId, i => i);
        var shortages = leafRequirements
            .Select(r =>
            {
                if (!itemMap.TryGetValue(r.Key, out var item))
                    return null;

                var available = currentStockByItem.TryGetValue(item.ItemId, out var stock) ? stock : 0;
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
        if (dto.Lines == null || dto.Lines.Count == 0)
            return BadRequest("Заказ должен содержать хотя бы одну позицию.");

        if (dto.OrderDate < DateTime.UtcNow)
            return BadRequest("Нельзя создать заказ на прошедшую дату/время.");
        if (dto.DueDate < dto.OrderDate)
            return BadRequest("Срок исполнения не может быть раньше даты заказа.");

        var groupedLines = dto.Lines
            .GroupBy(l => l.ItemId)
            .Select(g => new { ItemId = g.Key, Quantity = g.Sum(x => x.Quantity) })
            .ToList();

        if (groupedLines.Any(l => l.Quantity <= 0))
            return BadRequest("Количество по каждой позиции должно быть больше 0.");

        var itemIds = groupedLines.Select(l => l.ItemId).ToList();
        var existingItems = await _context.Item.Where(i => itemIds.Contains(i.ItemId)).Select(i => i.ItemId).ToListAsync();
        if (existingItems.Count != itemIds.Count)
            return BadRequest("Одна или несколько позиций заказа не найдены.");

        var invalidItem = await _context.Item
            .Where(i => itemIds.Contains(i.ItemId))
            .AnyAsync(i => !_context.BOM.Any(b => b.ParentId == i.ItemId));
        if (invalidItem)
            return BadRequest("Продажа запрещена: можно продавать только изделия/узлы.");

        var order = new Order
        {
            Status = "Draft",
            Comment = dto.Comment,
            CreatedAt = DateTime.UtcNow,
            OrderDate = DateTime.SpecifyKind(dto.OrderDate, DateTimeKind.Utc),
            DueDate = DateTime.SpecifyKind(dto.DueDate, DateTimeKind.Utc),
            Lines = groupedLines.Select(l => new OrderLine
            {
                ItemId = l.ItemId,
                Quantity = l.Quantity
            }).ToList()
        };

        _context.Orders.Add(order);
        await _context.SaveChangesAsync();
        await _context.Entry(order).Collection(o => o.Lines).Query().Include(l => l.Item).LoadAsync();
        return Ok(MapOrder(order));
    }

    [HttpPost("{orderId}/confirm")]
    public async Task<ActionResult<OrderDto>> ConfirmOrder(int orderId)
    {
        var order = await _context.Orders.Include(o => o.Lines).ThenInclude(l => l.Item).FirstOrDefaultAsync(o => o.OrderId == orderId);
        if (order == null)
            return NotFound("Заказ не найден.");
        if (order.Status != "Draft")
            return BadRequest("Подтверждать можно только заказ в статусе Черновик.");

        order.Status = "Confirmed";
        await _context.SaveChangesAsync();

        return Ok(MapOrder(order));
    }

    [HttpPost("{orderId}/ship")]
    public async Task<ActionResult<OrderDto>> ShipOrder(int orderId)
    {
        var order = await _context.Orders.Include(o => o.Lines).ThenInclude(l => l.Item).FirstOrDefaultAsync(o => o.OrderId == orderId);
        if (order == null)
            return NotFound("Заказ не найден.");
        if (order.Status != "Confirmed")
            return BadRequest("Отгружать можно только подтвержденный заказ.");

        var currentStockByItem = await _context.WarehouseOperations
            .GroupBy(o => o.ItemId)
            .Select(g => new
            {
                ItemId = g.Key,
                Quantity = g.Sum(x => x.OperationType == "Income" ? x.Quantity : -x.Quantity)
            })
            .ToDictionaryAsync(x => x.ItemId, x => x.Quantity);

        var requiredByItem = order.Lines
            .GroupBy(l => l.ItemId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));

        foreach (var required in requiredByItem)
        {
            var currentStock = currentStockByItem.TryGetValue(required.Key, out var qty) ? qty : 0;
            if (currentStock < required.Value)
            {
                var itemName = order.Lines.First(l => l.ItemId == required.Key).Item.ItemName;
                return BadRequest($"Недостаточно остатка для отгрузки \"{itemName}\". На складе: {currentStock}, требуется: {required.Value}.");
            }
        }

        await using var tx = await _context.Database.BeginTransactionAsync();
        try
        {
            order.Status = "Shipped";
            foreach (var line in order.Lines)
            {
                _context.WarehouseOperations.Add(new WarehouseOperation
                {
                    ItemId = line.ItemId,
                    OperationType = "Expense",
                    Quantity = line.Quantity,
                    Comment = string.IsNullOrWhiteSpace(order.Comment)
                        ? $"Отгрузка по заказу #{order.OrderId}"
                        : $"Отгрузка по заказу #{order.OrderId}. {order.Comment}",
                    CreatedAt = DateTime.UtcNow,
                    OperationDate = order.OrderDate
                });
            }

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
        var order = await _context.Orders.Include(o => o.Lines).ThenInclude(l => l.Item).FirstOrDefaultAsync(o => o.OrderId == orderId);
        if (order == null)
            return NotFound("Заказ не найден.");
        if (order.Status == "Shipped")
            return BadRequest("Отгруженный заказ нельзя отменить.");
        if (order.Status == "Cancelled")
            return BadRequest("Заказ уже отменен.");

        order.Status = "Cancelled";
        await _context.SaveChangesAsync();

        return Ok(MapOrder(order));
    }

    private static OrderDto MapOrder(Order order)
    {
        return new OrderDto
        {
            OrderId = order.OrderId,
            Status = order.Status,
            Comment = order.Comment,
            CreatedAt = order.CreatedAt,
            OrderDate = order.OrderDate,
            DueDate = order.DueDate,
            Lines = order.Lines.Select(l => new OrderLineDto
            {
                OrderLineId = l.OrderLineId,
                ItemId = l.ItemId,
                ItemName = l.Item.ItemName,
                Quantity = l.Quantity
            }).ToList()
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

    private static void AddLeafRequirementsWithUpperLevelStockNetting(
        int itemId,
        int requiredQuantity,
        IReadOnlyDictionary<int, List<(int ComponentId, int Quantity)>> childrenByParent,
        IDictionary<int, int> stockForNetting,
        IDictionary<int, int> leafRequirements)
    {
        if (requiredQuantity <= 0)
            return;

        var hasChildren = childrenByParent.TryGetValue(itemId, out var children) && children.Count > 0;

        if (hasChildren)
        {
            var availableAtThisLevel = stockForNetting.TryGetValue(itemId, out var current) ? current : 0;
            var consumed = Math.Min(availableAtThisLevel, requiredQuantity);
            if (consumed > 0)
            {
                stockForNetting[itemId] = availableAtThisLevel - consumed;
            }

            var remainingForExplosion = requiredQuantity - consumed;
            if (remainingForExplosion <= 0)
                return;

            foreach (var child in children!)
            {
                AddLeafRequirementsWithUpperLevelStockNetting(
                    child.ComponentId,
                    remainingForExplosion * child.Quantity,
                    childrenByParent,
                    stockForNetting,
                    leafRequirements);
            }

            return;
        }

        leafRequirements[itemId] = leafRequirements.TryGetValue(itemId, out var existing)
            ? existing + requiredQuantity
            : requiredQuantity;
    }

    private async Task<Dictionary<int, ItemBalance>> BuildBalancesAsync()
    {
        var warehouseOps = await _context.WarehouseOperations
            .Select(o => new { o.ItemId, o.OperationType, o.Quantity })
            .ToListAsync();

        var reserved = await _context.OrderLines
            .Where(l => l.Order.Status == "Confirmed")
            .GroupBy(l => l.ItemId)
            .Select(g => new { ItemId = g.Key, Quantity = g.Sum(x => x.Quantity) })
            .ToListAsync();

        var currentByItem = warehouseOps
            .GroupBy(o => o.ItemId)
            .ToDictionary(
                g => g.Key,
                g => g.Sum(x => x.OperationType == "Income" ? x.Quantity : -x.Quantity));

        var reservedByItem = reserved.ToDictionary(x => x.ItemId, x => x.Quantity);
        var itemIds = currentByItem.Keys.Union(reservedByItem.Keys).ToList();

        var result = new Dictionary<int, ItemBalance>();
        foreach (var itemId in itemIds)
        {
            var current = currentByItem.TryGetValue(itemId, out var c) ? c : 0;
            var reservedQty = reservedByItem.TryGetValue(itemId, out var r) ? r : 0;
            result[itemId] = new ItemBalance(current, reservedQty);
        }

        return result;
    }

    private sealed record ItemBalance(int CurrentQuantity, int ReservedQuantity)
    {
        public int AvailableQuantity => CurrentQuantity - ReservedQuantity;
    }
}
