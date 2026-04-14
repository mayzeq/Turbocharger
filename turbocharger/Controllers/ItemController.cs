using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Turbocharger.Domain.Entities;
using Turbocharger.Domain.ValueObjects;
using Turbocharger.Storage;

namespace Turbocharger.Controllers;

[Route("api/[controller]")]
[ApiController]
public class ItemController : ControllerBase
{
    private readonly AppDbContext _context;

    public ItemController(AppDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Получить все элементы (детали и узлы).
    /// </summary>
    /// <returns>Список всех элементов</returns>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ItemResponseDto>>> GetItems()
    {
        var items = await _context.Item
            .Select(i => new ItemResponseDto
            {
                ItemId = i.ItemId,
                ItemName = i.ItemName,
                CurrentQuantity = i.CurrentQuantity,
                PurchasePrice = i.PurchasePrice
            })
            .ToListAsync();

        return Ok(items);
    }

    /// <summary>
    /// Получить элемент по идентификатору.
    /// </summary>
    /// <param name="id">Идентификатор элемента</param>
    /// <returns>Элемент или 404</returns>
    [HttpGet("{id}")]
    public async Task<ActionResult<ItemResponseDto>> GetItem(int id)
    {
        var item = await _context.Item
            .Where(i => i.ItemId == id)
            .Select(i => new ItemResponseDto
            {
                ItemId = i.ItemId,
                ItemName = i.ItemName,
                CurrentQuantity = i.CurrentQuantity,
                PurchasePrice = i.PurchasePrice

            })
            .FirstOrDefaultAsync();

        if (item == null)
            return NotFound($"Элемент с ID {id} не найден");

        return Ok(item);
    }

    /// <summary>
    /// Создать новый элемент.
    /// </summary>
    /// <param name="dto">Данные для создания</param>
    /// <returns>Созданный элемент</returns>
    [HttpPost]
    public async Task<ActionResult<ItemResponseDto>> PostItem([FromBody] ItemCreateDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.ItemName))
            return BadRequest("Название элемента не может быть пустым");

        var item = new Item
        {
            ItemName = dto.ItemName,
            PurchasePrice = dto.PurchasePrice
        };

        _context.Item.Add(item);
        await _context.SaveChangesAsync();

        var response = new ItemResponseDto
        {
            ItemId = item.ItemId,
            ItemName = item.ItemName,
            CurrentQuantity = item.CurrentQuantity,
            PurchasePrice = item.PurchasePrice
        };

        return CreatedAtAction(nameof(GetItem), new { id = response.ItemId }, response);
    }

    /// <summary>
    /// Обновить существующий элемент.
    /// </summary>
    /// <param name="id">Идентификатор элемента</param>
    /// <param name="dto">Новые данные</param>
    /// <returns>204 No Content или 404</returns>
    [HttpPut("{id}")]
    public async Task<IActionResult> PutItem(int id, [FromBody] ItemCreateDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.ItemName))
            return BadRequest("Название элемента не может быть пустым");

        var item = await _context.Item.FindAsync(id);
        if (item == null)
            return NotFound($"Элемент с ID {id} не найден");

        item.ItemName = dto.ItemName;
        item.PurchasePrice = dto.PurchasePrice;

        await _context.SaveChangesAsync();

        return Ok(new ItemResponseDto
        {
            ItemId = item.ItemId,
            ItemName = item.ItemName,
            CurrentQuantity = item.CurrentQuantity,
            PurchasePrice = item.PurchasePrice
        });
    }

    /// <summary>
    /// Удалить элемент, если он не используется в структуре сборки.
    /// </summary>
    /// <param name="id">Идентификатор элемента</param>
    /// <returns>204 No Content или 400/404</returns>
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteItem(int id)
    {
        var item = await _context.Item.FindAsync(id);
        if (item == null)
            return NotFound($"Элемент с ID {id} не найден");

        if (item.CurrentQuantity > 0)
            return BadRequest($"Элемент нельзя удалить: на складе есть остаток ({item.CurrentQuantity}).");

        // Проверяем, используется ли элемент в сборках
        bool isUsed = await _context.BOM.AnyAsync(b => b.ParentId == id || b.ComponentId == id);
        if (isUsed)
            return BadRequest("Элемент используется в структуре сборки и не может быть удалён");

        _context.Item.Remove(item);
        await _context.SaveChangesAsync();

        return NoContent();
    }
}