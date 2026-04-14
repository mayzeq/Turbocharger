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
            UnitPrice = o.UnitPrice,
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
            UnitPrice = o.UnitPrice,
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

            if (dto.UnitPrice < 0)
                return BadRequest("Цена за единицу не может быть отрицательной.");

            // Проверяем тип операции
            var validTypes = new[] { "Income", "Expense" };
            if (!validTypes.Contains(dto.OperationType))
                return BadRequest("Неверный тип операции. Допустимые: Income, Expense.");

            // Проверка количества для расхода
            if (dto.OperationType == "Expense" && dto.Quantity > item.CurrentQuantity)
                return BadRequest($"Невозможно провести расход: недостаточно на складе. Доступно: {item.CurrentQuantity}, требуется: {dto.Quantity}.");

            // Создаём операцию
            var operation = new WarehouseOperation
            {
                ItemId = dto.ItemId,
                OperationType = dto.OperationType,
                Quantity = dto.Quantity,
                UnitPrice = dto.UnitPrice,
                Comment = dto.Comment,
                CreatedAt = DateTime.UtcNow,
                OperationDate = DateTime.SpecifyKind(dto.OperationDate, DateTimeKind.Utc)
            };

            _context.WarehouseOperations.Add(operation);

            // Обновляем остаток
            item.CurrentQuantity += dto.OperationType switch
            {
                "Income" => dto.Quantity,
                "Expense" => -dto.Quantity,
                _ => 0
            };

            // Обновляем цену при приходе
            if (dto.OperationType == "Income")
            {
                item.PurchasePrice = dto.UnitPrice;
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

            if (dto.UnitPrice < 0)
                return BadRequest("Цена за единицу не может быть отрицательной.");

            // Проверяем тип операции
            var validTypes = new[] { "Income", "Expense" };
            if (!validTypes.Contains(dto.OperationType))
                return BadRequest("Неверный тип операции. Допустимые: Income, Expense.");

            // Отменяем старую операцию — возвращаем количество
            item.CurrentQuantity -= operation.OperationType switch
            {
                "Income" => operation.Quantity,
                "Expense" => -operation.Quantity,
                _ => 0
            };

            // Проверяем, что остаток не ушёл в минус после отмены
            if (item.CurrentQuantity < 0)
            {
                // Откатываем изменение
                item.CurrentQuantity += operation.OperationType switch
                {
                    "Income" => operation.Quantity,
                    "Expense" => -operation.Quantity,
                    _ => 0
                };
                return BadRequest($"Невозможно изменить операцию: после отмены текущей операции остаток уйдёт в минус ({item.CurrentQuantity}). Сначала измените или удалите последующие операции расхода.");
            }

            // Проверяем количество для расхода
            if (dto.OperationType == "Expense" && dto.Quantity > item.CurrentQuantity)
            {
                // Откатываем изменение
                item.CurrentQuantity += operation.OperationType switch
                {
                    "Income" => operation.Quantity,
                    "Expense" => -operation.Quantity,
                    _ => 0
                };
                return BadRequest($"Невозможно применить операцию: недостаточно на складе. Доступно: {item.CurrentQuantity}, требуется: {dto.Quantity}.");
            }

            // Применяем новую операцию
            item.CurrentQuantity += dto.OperationType switch
            {
                "Income" => dto.Quantity,
                "Expense" => -dto.Quantity,
                _ => 0
            };

            // Обновляем цену при приходе
            if (dto.OperationType == "Income")
            {
                item.PurchasePrice = dto.UnitPrice;
            }

            // Обновляем операцию
            operation.ItemId = dto.ItemId;
            operation.OperationType = dto.OperationType;
            operation.Quantity = dto.Quantity;
            operation.UnitPrice = dto.UnitPrice;
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
                UnitPrice = operation.UnitPrice,
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

            // Рассчитываем остаток после отмены операции
            var newQuantity = operation.Item.CurrentQuantity - operation.OperationType switch
            {
                "Income" => operation.Quantity,
                "Expense" => -operation.Quantity,
                _ => 0
            };

            // Проверяем, что остаток не уйдёт в минус
            if (newQuantity < 0)
            {
                return BadRequest($"Невозможно удалить операцию: после отмены остаток уйдёт в минус ({newQuantity}). Сначала удалите или измените последующие операции расхода.");
            }

            // Применяем отмену
            operation.Item.CurrentQuantity = newQuantity;

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
                itemName = i.ItemName,
                currentQuantity = i.CurrentQuantity,
                purchasePrice = i.PurchasePrice,
                totalValue = i.CurrentQuantity * i.PurchasePrice
            })
            .ToListAsync();

        return Ok(stock);
    }
}