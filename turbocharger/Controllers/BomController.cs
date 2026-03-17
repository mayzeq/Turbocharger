using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Turbocharger.Domain.Entities;
using Turbocharger.Domain.ValueObjects;
using Turbocharger.Storage;

namespace Turbocharger.Controllers;

[Route("api/[controller]")]
[ApiController]
public class BomController : ControllerBase
{
    private readonly AppDbContext _context;

    public BomController(AppDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Получить все связи BOM.
    /// </summary>
    /// <returns>Список всех связей</returns>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<BomResponseDto>>> GetBoms()
    {
        var boms = await _context.BOM
            .Include(b => b.Parent)
            .Include(b => b.Component)
            .Select(b => new BomResponseDto
            {
                BomId = b.bom_id,
                ParentId = b.parent_id,
                ParentName = b.Parent != null ? b.Parent.item_name : null,
                ComponentId = b.component_id,
                ComponentName = b.Component.item_name,
                Quantity = b.quantity
            })
            .ToListAsync();

        return Ok(boms);
    }

    /// <summary>
    /// Получить конкретную связь BOM по идентификатору.
    /// </summary>
    /// <param name="id">Идентификатор связи</param>
    /// <returns>Связь BOM или 404</returns>
    [HttpGet("{id}")]
    public async Task<ActionResult<BomResponseDto>> GetBom(int id)
    {
        var bom = await _context.BOM
            .Include(b => b.Parent)
            .Include(b => b.Component)
            .Where(b => b.bom_id == id)
            .Select(b => new BomResponseDto
            {
                BomId = b.bom_id,
                ParentId = b.parent_id,
                ParentName = b.Parent != null ? b.Parent.item_name : null,
                ComponentId = b.component_id,
                ComponentName = b.Component.item_name,
                Quantity = b.quantity
            })
            .FirstOrDefaultAsync();

        if (bom == null)
            return NotFound($"Связь BOM с ID {id} не найдена");

        return Ok(bom);
    }

    /// <summary>
    /// Получить все дочерние компоненты для указанного родителя.
    /// </summary>
    /// <param name="parentId">Идентификатор родителя</param>
    /// <returns>Список дочерних компонентов</returns>
    [HttpGet("by-parent/{parentId}")]
    public async Task<ActionResult<IEnumerable<BomResponseDto>>> GetBomsByParent(int parentId)
    {
        var parentExists = await _context.Item.AnyAsync(i => i.item_id == parentId);
        if (!parentExists)
            return NotFound($"Родитель с ID {parentId} не найден");

        var boms = await _context.BOM
            .Include(b => b.Component)
            .Where(b => b.parent_id == parentId)
            .Select(b => new BomResponseDto
            {
                BomId = b.bom_id,
                ParentId = b.parent_id,
                ComponentId = b.component_id,
                ComponentName = b.Component.item_name,
                Quantity = b.quantity
            })
            .ToListAsync();

        return Ok(boms);
    }

    /// <summary>
    /// Создать новую связь в структуре сборки.
    /// </summary>
    /// <param name="dto">Данные для создания</param>
    /// <returns>Созданная связь</returns>
    [HttpPost]
    public async Task<ActionResult<BomResponseDto>> PostBom([FromBody] BomCreateDto dto)
    {
        // Проверка существования компонента
        var component = await _context.Item.FindAsync(dto.ComponentId);
        if (component == null)
            return BadRequest($"Компонент с ID {dto.ComponentId} не существует");

        // Если указан родитель, проверяем его существование
        if (dto.ParentId.HasValue)
        {
            var parent = await _context.Item.FindAsync(dto.ParentId.Value);
            if (parent == null)
                return BadRequest($"Родительский элемент с ID {dto.ParentId.Value} не существует");

            // Проверка на циклическую зависимость
            if (await WouldCreateCycle(dto.ParentId.Value, dto.ComponentId))
                return BadRequest("Создание циклической зависимости запрещено");
        }

        // Проверка на дубликат
        bool exists = await _context.BOM.AnyAsync(b =>
            b.parent_id == dto.ParentId &&
            b.component_id == dto.ComponentId);

        if (exists)
            return BadRequest("Такая связь уже существует");

        var bom = new Bom
        {
            parent_id = dto.ParentId,
            component_id = dto.ComponentId,
            quantity = dto.Quantity > 0 ? dto.Quantity : 1
        };

        _context.BOM.Add(bom);
        await _context.SaveChangesAsync();

        // Загружаем навигационные свойства для ответа
        await _context.Entry(bom).Reference(b => b.Parent).LoadAsync();
        await _context.Entry(bom).Reference(b => b.Component).LoadAsync();

        var response = new BomResponseDto
        {
            BomId = bom.bom_id,
            ParentId = bom.parent_id,
            ParentName = bom.Parent?.item_name,
            ComponentId = bom.component_id,
            ComponentName = bom.Component.item_name,
            Quantity = bom.quantity
        };

        return CreatedAtAction(nameof(GetBom), new { id = response.BomId }, response);
    }

    /// <summary>
    /// Обновить существующую связь BOM.
    /// </summary>
    /// <param name="id">Идентификатор связи</param>
    /// <param name="dto">Новые данные</param>
    /// <returns>204 No Content или 400/404</returns>
    [HttpPut("{id}")]
    public async Task<IActionResult> PutBom(int id, [FromBody] BomCreateDto dto)
    {
        var bom = await _context.BOM.FindAsync(id);
        if (bom == null)
            return NotFound($"Связь BOM с ID {id} не найдена");

        // Проверка существования компонента
        if (!await _context.Item.AnyAsync(i => i.item_id == dto.ComponentId))
            return BadRequest($"Компонент с ID {dto.ComponentId} не существует");

        if (dto.ParentId.HasValue && !await _context.Item.AnyAsync(i => i.item_id == dto.ParentId))
            return BadRequest($"Родительский элемент с ID {dto.ParentId} не существует");

        // Проверка на циклическую зависимость (если меняется структура)
        if ((bom.parent_id != dto.ParentId || bom.component_id != dto.ComponentId) &&
            dto.ParentId.HasValue &&
            await WouldCreateCycle(dto.ParentId.Value, dto.ComponentId))
            return BadRequest("Создание циклической зависимости запрещено");

        bom.parent_id = dto.ParentId;
        bom.component_id = dto.ComponentId;
        bom.quantity = dto.Quantity > 0 ? dto.Quantity : 1;

        await _context.SaveChangesAsync();

        return NoContent();
    }

    /// <summary>
    /// Удалить связь BOM.
    /// </summary>
    /// <param name="id">Идентификатор связи</param>
    /// <returns>204 No Content или 404</returns>
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteBom(int id)
    {
        var bom = await _context.BOM.FindAsync(id);
        if (bom == null)
            return NotFound($"Связь BOM с ID {id} не найдена");

        _context.BOM.Remove(bom);
        await _context.SaveChangesAsync();

        return NoContent();
    }

    /// <summary>
    /// Проверка на создание циклической зависимости.
    /// </summary>
    private async Task<bool> WouldCreateCycle(int parentId, int componentId)
    {
        // Если компонент пытается стать родителем самого себя
        if (parentId == componentId)
            return true;

        // Проверяем, не является ли родитель потомком компонента
        var descendants = new HashSet<int>();
        await GetDescendants(componentId, descendants);

        return descendants.Contains(parentId);
    }

    private async Task GetDescendants(int itemId, HashSet<int> descendants)
    {
        var children = await _context.BOM
            .Where(b => b.parent_id == itemId)
            .Select(b => b.component_id)
            .ToListAsync();

        foreach (var childId in children)
        {
            if (descendants.Add(childId))
            {
                await GetDescendants(childId, descendants);
            }
        }
    }
}