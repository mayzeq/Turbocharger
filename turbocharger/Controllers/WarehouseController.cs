using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Turbocharger.Storage;
using Turbocharger.Domain.Entities;
using Turbocharger.ValueObjects;

namespace Turbocharger.Controllers;

[Route("api/[controller]")]
[ApiController]
public class WarehouseController : ControllerBase
{
    private readonly AppDbContext _context;

    public WarehouseController(AppDbContext context) => _context = context;

    /// <summary>
    /// Получить все складские операции.
    /// </summary>
    [HttpGet("operations")]
    public async Task<ActionResult<IEnumerable<WarehouseOperationDto>>> GetOperations()
    {
        var operations = await _context.WarehouseOperations
            .Include(o => o.Item)
            .OrderByDescending(o => o.OperationDate)
            .ToListAsync();

        var result = operations.Select(o => new WarehouseOperationDto
        {
            OperationId = o.OperationId,
            ItemId = o.ItemId,
            ItemName = o.Item.ItemName,
            OperationType = o.OperationType,
            Quantity = o.Quantity,
            Comment = o.Comment,
            CreatedAt = o.CreatedAt,
            OperationDate = o.OperationDate
        });

        return Ok(result);
    }

    /// <summary>
    /// Получить операции по элементу.
    /// </summary>
    [HttpGet("operations/{itemId}")]
    public async Task<ActionResult<IEnumerable<WarehouseOperationDto>>> GetOperationsByItem(int itemId)
    {
        var operations = await _context.WarehouseOperations
            .Include(o => o.Item)
            .Where(o => o.ItemId == itemId)
            .OrderByDescending(o => o.OperationDate)
            .ToListAsync();

        var result = operations.Select(o => new WarehouseOperationDto
        {
            OperationId = o.OperationId,
            ItemId = o.ItemId,
            ItemName = o.Item.ItemName,
            OperationType = o.OperationType,
            Quantity = o.Quantity,
            Comment = o.Comment,
            CreatedAt = o.CreatedAt,
            OperationDate = o.OperationDate
        });

        return Ok(result);
    }

    /// <summary>
    /// Создать складскую операцию (приход, расход, корректировка).
    /// </summary>
    [HttpPost("operations")]
    public async Task<ActionResult<WarehouseOperationDto>> CreateOperation([FromBody] WarehouseOperationCreateDto dto)
    {
        try
        {
            var item = await _context.Item.FindAsync(dto.ItemId);
            if (item == null)
                return BadRequest("Элемент не найден.");

            if (dto.Quantity <= 0)
                return BadRequest("Количество должно быть больше 0.");

            // Проверяем тип операции
            var validTypes = new[] { "Income", "Expense" };
            if (!validTypes.Contains(dto.OperationType))
                return BadRequest("Неверный тип операции. Допустимые: Income, Expense.");

            // Проверка количества для расхода
            var currentQuantity = await GetCurrentQuantity(dto.ItemId);
            if (dto.OperationType == "Expense" && dto.Quantity > currentQuantity)
                return BadRequest($"Невозможно провести расход: недостаточно на складе. Доступно: {currentQuantity}, требуется: {dto.Quantity}.");

            // Создаём операцию
            var operation = new WarehouseOperation
            {
                ItemId = dto.ItemId,
                OperationType = dto.OperationType,
                Quantity = dto.Quantity,
                Comment = dto.Comment,
                CreatedAt = DateTime.UtcNow,
                OperationDate = DateTime.SpecifyKind(dto.OperationDate, DateTimeKind.Utc)
            };

            _context.WarehouseOperations.Add(operation);

            await _context.SaveChangesAsync();

            return Ok(new WarehouseOperationDto
            {
                OperationId = operation.OperationId,
                ItemId = operation.ItemId,
                ItemName = item.ItemName,
                OperationType = operation.OperationType,
                Quantity = operation.Quantity,
                Comment = operation.Comment,
                CreatedAt = operation.CreatedAt,
                OperationDate = operation.OperationDate
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Внутренняя ошибка: {ex.Message}");
        }
    }

    /// <summary>
    /// Изменить складскую операцию.
    /// </summary>
    [HttpPut("operations/{operationId}")]
    public async Task<ActionResult<WarehouseOperationDto>> UpdateOperation(int operationId, [FromBody] WarehouseOperationCreateDto dto)
    {
        try
        {
            var operation = await _context.WarehouseOperations
                .Include(o => o.Item)
                .FirstOrDefaultAsync(o => o.OperationId == operationId);

            if (operation == null)
                return NotFound("Операция не найдена.");

            var item = await _context.Item.FindAsync(dto.ItemId);
            if (item == null)
                return BadRequest("Элемент не найден.");

            if (dto.Quantity <= 0)
                return BadRequest("Количество должно быть больше 0.");

            // Проверяем тип операции
            var validTypes = new[] { "Income", "Expense" };
            if (!validTypes.Contains(dto.OperationType))
                return BadRequest("Неверный тип операции. Допустимые: Income, Expense.");
            var projectedCurrent = await GetCurrentQuantity(dto.ItemId, operationId);
            if (dto.OperationType == "Expense" && dto.Quantity > projectedCurrent)
                return BadRequest($"Невозможно применить операцию: недостаточно на складе. Доступно: {projectedCurrent}, требуется: {dto.Quantity}.");

            // Обновляем операцию
            operation.ItemId = dto.ItemId;
            operation.OperationType = dto.OperationType;
            operation.Quantity = dto.Quantity;
            operation.Comment = dto.Comment;
            operation.OperationDate = DateTime.SpecifyKind(dto.OperationDate, DateTimeKind.Utc);

            await _context.SaveChangesAsync();

            return Ok(new WarehouseOperationDto
            {
                OperationId = operation.OperationId,
                ItemId = operation.ItemId,
                ItemName = item.ItemName,
                OperationType = operation.OperationType,
                Quantity = operation.Quantity,
                Comment = operation.Comment,
                CreatedAt = operation.CreatedAt,
                OperationDate = operation.OperationDate
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Внутренняя ошибка: {ex.Message}");
        }
    }

    /// <summary>
    /// Удалить складскую операцию.
    /// </summary>
    [HttpDelete("operations/{operationId}")]
    public async Task<IActionResult> DeleteOperation(int operationId)
    {
        try
        {
            var operation = await _context.WarehouseOperations
                .Include(o => o.Item)
                .FirstOrDefaultAsync(o => o.OperationId == operationId);

            if (operation == null)
                return NotFound("Операция не найдена.");

            var newQuantity = await GetCurrentQuantity(operation.ItemId, operationId);

            // Проверяем, что остаток не уйдёт в минус
            if (newQuantity < 0)
            {
                return BadRequest($"Невозможно удалить операцию: после отмены остаток уйдёт в минус ({newQuantity}). Сначала удалите или измените последующие операции расхода.");
            }

            _context.WarehouseOperations.Remove(operation);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Операция удалена" });
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Внутренняя ошибка: {ex.Message}");
        }
    }

    /// <summary>
    /// Получить текущие остатки по всем элементам.
    /// </summary>
    [HttpGet("stock")]
    public async Task<ActionResult<IEnumerable<object>>> GetStock()
    {
        var stock = await _context.Item
            .Select(i => new
            {
                itemId = i.ItemId,
                itemName = i.ItemName
            })
            .ToListAsync();

        var operationRows = await _context.WarehouseOperations
            .Select(o => new { o.ItemId, o.OperationType, o.Quantity })
            .ToListAsync();
        var reservedRows = await _context.OrderLines
            .Where(l => l.Order.Status == "Confirmed")
            .GroupBy(l => l.ItemId)
            .Select(g => new { ItemId = g.Key, Quantity = g.Sum(x => x.Quantity) })
            .ToListAsync();

        var currentByItem = operationRows
            .GroupBy(o => o.ItemId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.OperationType == "Income" ? x.Quantity : -x.Quantity));
        var reservedByItem = reservedRows.ToDictionary(x => x.ItemId, x => x.Quantity);

        var result = stock.Select(i =>
        {
            var current = currentByItem.TryGetValue(i.itemId, out var c) ? c : 0;
            var reserved = reservedByItem.TryGetValue(i.itemId, out var r) ? r : 0;
            return new
            {
                i.itemId,
                i.itemName,
                currentQuantity = current,
                reservedQuantity = reserved,
                availableQuantity = current - reserved
            };
        });

        return Ok(result);
    }

    private async Task<int> GetCurrentQuantity(int itemId, int? excludingOperationId = null)
    {
        var query = _context.WarehouseOperations.AsQueryable().Where(o => o.ItemId == itemId);
        if (excludingOperationId.HasValue)
            query = query.Where(o => o.OperationId != excludingOperationId.Value);

        return await query.SumAsync(o => o.OperationType == "Income" ? o.Quantity : -o.Quantity);
    }
}