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
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync();

        var result = operations.Select(o => new WarehouseOperationDto
        {
            OperationId = o.OperationId,
            ItemId = o.ItemId,
            ItemName = o.Item.ItemName,
            OperationType = o.OperationType,
            Quantity = o.Quantity,
            UnitPrice = o.UnitPrice,
            Comment = o.Comment,
            CreatedAt = o.CreatedAt
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
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync();

        var result = operations.Select(o => new WarehouseOperationDto
        {
            OperationId = o.OperationId,
            ItemId = o.ItemId,
            ItemName = o.Item.ItemName,
            OperationType = o.OperationType,
            Quantity = o.Quantity,
            UnitPrice = o.UnitPrice,
            Comment = o.Comment,
            CreatedAt = o.CreatedAt
        });

        return Ok(result);
    }

    /// <summary>
    /// Получить все партии (для FIFO).
    /// </summary>
    [HttpGet("batches")]
    public async Task<ActionResult<IEnumerable<StockBatchDto>>> GetBatches()
    {
        var batches = await _context.StockBatches
            .Include(b => b.Item)
            .Where(b => b.Quantity > 0)
            .OrderBy(b => b.CreatedAt)
            .Select(b => new StockBatchDto
            {
                StockBatchId = b.StockBatchId,
                ItemId = b.ItemId,
                ItemName = b.Item.ItemName,
                Quantity = b.Quantity,
                InitialQuantity = b.InitialQuantity,
                UnitPrice = b.UnitPrice,
                CreatedAt = b.CreatedAt
            })
            .ToListAsync();

        return Ok(batches);
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

            // Проверяем тип операции
            var validTypes = new[] { "Income", "Expense", "Adjustment" };
            if (!validTypes.Contains(dto.OperationType))
                return BadRequest("Неверный тип операции. Допустимые: Income, Expense, Adjustment.");

            // Проверка количества для расхода
            if (dto.OperationType == "Expense" && dto.Quantity > item.CurrentQuantity)
                return BadRequest($"Недостаточно на складе. Доступно: {item.CurrentQuantity}");

            // Создаём операцию
            var operation = new WarehouseOperation
            {
                ItemId = dto.ItemId,
                OperationType = dto.OperationType,
                Quantity = dto.Quantity,
                UnitPrice = dto.UnitPrice,
                Comment = dto.Comment,
                CreatedAt = DateTime.UtcNow
            };

            _context.WarehouseOperations.Add(operation);

            // Обновляем остаток
            item.CurrentQuantity += dto.OperationType switch
            {
                "Income" => dto.Quantity,
                "Expense" => -dto.Quantity,
                "Adjustment" => dto.Quantity,
                _ => 0
            };

            // Логика FIFO для прихода/расхода
            if (dto.OperationType == "Income")
            {
                // Создаём новую партию
                var batch = new StockBatch
                {
                    ItemId = dto.ItemId,
                    Quantity = dto.Quantity,
                    InitialQuantity = dto.Quantity,
                    UnitPrice = dto.UnitPrice,
                    CreatedAt = DateTime.UtcNow
                };
                _context.StockBatches.Add(batch);

                // Обновляем цену при приходе
                item.PurchasePrice = dto.UnitPrice;
            }
            else if (dto.OperationType == "Adjustment")
            {
                // При корректировке также обновляем цену
                item.PurchasePrice = dto.UnitPrice;

                // Создаем корректировочную партию
                var adjustmentBatch = new StockBatch
                {
                    ItemId = dto.ItemId,
                    Quantity = dto.Quantity,
                    InitialQuantity = dto.Quantity,
                    UnitPrice = dto.UnitPrice,
                    CreatedAt = DateTime.UtcNow
                };
                _context.StockBatches.Add(adjustmentBatch);
            }

            await _context.SaveChangesAsync();

            return Ok(new WarehouseOperationDto
            {
                OperationId = operation.OperationId,
                ItemId = operation.ItemId,
                ItemName = item.ItemName,
                OperationType = operation.OperationType,
                Quantity = operation.Quantity,
                UnitPrice = operation.UnitPrice,
                Comment = operation.Comment,
                CreatedAt = operation.CreatedAt
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Внутренняя ошибка: {ex.Message}");
        }
    }

    /// <summary>
    /// Обработка расхода по FIFO.
    /// </summary>
    private async Task ProcessFifoExpense(int itemId, int quantity)
    {
        var batches = await _context.StockBatches
            .Where(b => b.ItemId == itemId && b.Quantity > 0)
            .OrderBy(b => b.CreatedAt)
            .ToListAsync();

        var remaining = quantity;

        foreach (var batch in batches)
        {
            if (remaining <= 0)
                break;

            var toDeduct = Math.Min(batch.Quantity, remaining);
            batch.Quantity -= toDeduct;
            remaining -= toDeduct;
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
                itemName = i.ItemName,
                currentQuantity = i.CurrentQuantity,
                purchasePrice = i.PurchasePrice,
                totalValue = i.CurrentQuantity * i.PurchasePrice
            })
            .ToListAsync();

        return Ok(stock);
    }
}