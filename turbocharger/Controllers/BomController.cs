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

    [HttpGet]
    public async Task<ActionResult<IEnumerable<BomResponseDto>>> GetBoms()
    {
        var boms = await _context.BOM
            .Include(b => b.Parent)
            .Include(b => b.Component)
            .Select(b => new BomResponseDto
            {
                BomId = b.BomId,
                ParentId = b.ParentId,
                ParentName = b.Parent != null ? b.Parent.ItemName : null,
                ComponentId = b.ComponentId,
                ComponentName = b.Component.ItemName,
                Quantity = b.Quantity
            })
            .ToListAsync();

        return Ok(boms);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<BomResponseDto>> GetBom(int id)
    {
        var bom = await _context.BOM
            .Include(b => b.Parent)
            .Include(b => b.Component)
            .Where(b => b.BomId == id)
            .Select(b => new BomResponseDto
            {
                BomId = b.BomId,
                ParentId = b.ParentId,
                ParentName = b.Parent != null ? b.Parent.ItemName : null,
                ComponentId = b.ComponentId,
                ComponentName = b.Component.ItemName,
                Quantity = b.Quantity
            })
            .FirstOrDefaultAsync();

        if (bom == null)
            return NotFound($"˜˜˜˜˜ BOM ˜ ID {id} ˜˜ ˜˜˜˜˜˜˜");

        return Ok(bom);
    }

    [HttpGet("by-parent/{parentId}")]
    public async Task<ActionResult<IEnumerable<BomResponseDto>>> GetBomsByParent(int parentId)
    {
        var parentExists = await _context.Item.AnyAsync(i => i.ItemId == parentId);
        if (!parentExists)
            return NotFound($"˜˜˜˜˜˜˜˜ ˜ ID {parentId} ˜˜ ˜˜˜˜˜˜");

        var boms = await _context.BOM
            .Include(b => b.Component)
            .Where(b => b.ParentId == parentId)
            .Select(b => new BomResponseDto
            {
                BomId = b.BomId,
                ParentId = b.ParentId,
                ComponentId = b.ComponentId,
                ComponentName = b.Component.ItemName,
                Quantity = b.Quantity
            })
            .ToListAsync();

        return Ok(boms);
    }

    [HttpPost]
    public async Task<ActionResult<BomResponseDto>> PostBom([FromBody] BomCreateDto dto)
    {
        if (dto.Quantity <= 0)
            return BadRequest("˜˜˜˜˜˜˜˜˜˜ ˜ ˜˜˜˜˜˜˜˜˜ ˜˜˜˜˜˜ ˜˜˜˜ ˜˜˜˜˜˜ 0.");

        var component = await _context.Item.FindAsync(dto.ComponentId);
        if (component == null)
            return BadRequest($"˜˜˜˜˜˜˜˜˜ ˜ ID {dto.ComponentId} ˜˜ ˜˜˜˜˜˜˜˜˜˜");

        if (dto.ParentId.HasValue)
        {
            var parent = await _context.Item.FindAsync(dto.ParentId.Value);
            if (parent == null)
                return BadRequest($"˜˜˜˜˜˜˜˜˜˜˜˜ ˜˜˜˜˜˜˜ ˜ ID {dto.ParentId.Value} ˜˜ ˜˜˜˜˜˜˜˜˜˜");

            if (await WouldCreateCycle(dto.ParentId.Value, dto.ComponentId))
                return BadRequest("˜˜˜˜˜˜˜˜˜˜ ˜˜˜˜ ˜˜˜˜˜ ˜˜˜˜˜˜˜˜ ˜ ˜˜˜˜˜");
        }

        var exists = await _context.BOM.AnyAsync(b =>
            b.ParentId == dto.ParentId &&
            b.ComponentId == dto.ComponentId);

        if (exists)
            return BadRequest("˜˜˜˜˜ ˜˜˜˜˜ ˜˜˜ ˜˜˜˜˜˜˜˜˜˜");

        var bom = new Bom
        {
            ParentId = dto.ParentId,
            ComponentId = dto.ComponentId,
            Quantity = dto.Quantity
        };

        _context.BOM.Add(bom);
        await _context.SaveChangesAsync();

        await _context.Entry(bom).Reference(b => b.Parent).LoadAsync();
        await _context.Entry(bom).Reference(b => b.Component).LoadAsync();

        var response = new BomResponseDto
        {
            BomId = bom.BomId,
            ParentId = bom.ParentId,
            ParentName = bom.Parent?.ItemName,
            ComponentId = bom.ComponentId,
            ComponentName = bom.Component.ItemName,
            Quantity = bom.Quantity
        };

        return CreatedAtAction(nameof(GetBom), new { id = response.BomId }, response);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> PutBom(int id, [FromBody] BomCreateDto dto)
    {
        if (dto.Quantity <= 0)
            return BadRequest("˜˜˜˜˜˜˜˜˜˜ ˜ ˜˜˜˜˜˜˜˜˜ ˜˜˜˜˜˜ ˜˜˜˜ ˜˜˜˜˜˜ 0.");

        var bom = await _context.BOM.FindAsync(id);
        if (bom == null)
            return NotFound($"˜˜˜˜˜ BOM ˜ ID {id} ˜˜ ˜˜˜˜˜˜˜");

        if (!await _context.Item.AnyAsync(i => i.ItemId == dto.ComponentId))
            return BadRequest($"˜˜˜˜˜˜˜˜˜ ˜ ID {dto.ComponentId} ˜˜ ˜˜˜˜˜˜˜˜˜˜");

        if (dto.ParentId.HasValue && !await _context.Item.AnyAsync(i => i.ItemId == dto.ParentId))
            return BadRequest($"˜˜˜˜˜˜˜˜˜˜˜˜ ˜˜˜˜˜˜˜ ˜ ID {dto.ParentId} ˜˜ ˜˜˜˜˜˜˜˜˜˜");

        if ((bom.ParentId != dto.ParentId || bom.ComponentId != dto.ComponentId) &&
            dto.ParentId.HasValue &&
            await WouldCreateCycle(dto.ParentId.Value, dto.ComponentId))
            return BadRequest("˜˜˜˜˜˜˜˜˜˜ ˜˜˜˜ ˜˜˜˜˜ ˜˜˜˜˜˜˜˜ ˜ ˜˜˜˜˜");

        bom.ParentId = dto.ParentId;
        bom.ComponentId = dto.ComponentId;
        bom.Quantity = dto.Quantity;

        await _context.SaveChangesAsync();

        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteBom(int id)
    {
        var bom = await _context.BOM
            .Include(b => b.Parent)
            .Include(b => b.Component)
            .FirstOrDefaultAsync(b => b.BomId == id);
        if (bom == null)
            return NotFound($"˜˜˜˜˜ BOM ˜ ID {id} ˜˜ ˜˜˜˜˜˜˜");

        var parentQty = bom.Parent?.CurrentQuantity ?? 0;
        var parentReserved = bom.Parent?.ReservedQuantity ?? 0;
        var componentQty = bom.Component.CurrentQuantity;
        var componentReserved = bom.Component.ReservedQuantity;
        if (parentQty > 0 || parentReserved > 0 || componentQty > 0 || componentReserved > 0)
            return BadRequest($"Íåëüçÿ óäàëèòü ñâÿçü: åñòü îñòàòêè/ðåçåðâû. Ðîäèòåëü: îñòàòîê {parentQty}, ðåçåðâ {parentReserved}; êîìïîíåíò: îñòàòîê {componentQty}, ðåçåðâ {componentReserved}.");

        _context.BOM.Remove(bom);
        await _context.SaveChangesAsync();

        return NoContent();
    }

    private async Task<bool> WouldCreateCycle(int parentId, int componentId)
    {
        if (parentId == componentId)
            return true;

        var descendants = new HashSet<int>();
        await GetDescendants(componentId, descendants);

        return descendants.Contains(parentId);
    }

    private async Task GetDescendants(int itemId, HashSet<int> descendants)
    {
        var children = await _context.BOM
            .Where(b => b.ParentId == itemId)
            .Select(b => b.ComponentId)
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
