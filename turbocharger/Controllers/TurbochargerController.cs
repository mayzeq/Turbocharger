using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Turbocharger.Domain.ValueObjects;
using Turbocharger.Storage;

namespace Turbocharger.Controllers;

[Route("api/[controller]")]
[ApiController]
public class TurbochargerController : ControllerBase
{
    private readonly AppDbContext _context;

    public TurbochargerController(AppDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Получить полное дерево состава турбокомпрессора с учетом весов.
    /// </summary>
    /// <param name="productId">ID изделия (по умолчанию 1 - Турбокомпрессор)</param>
    /// <returns>Дерево состава</returns>
    [HttpGet("tree/{productId:int=1}")]
    public async Task<ActionResult<BomTreeDto>> GetProductTree(int productId = 1)
    {
        var product = await _context.Item.FindAsync(productId);
        if (product == null)
            return NotFound($"Изделие с ID {productId} не найдено");

        var tree = await BuildTree(productId, 1, 0);
        return Ok(tree);
    }

    /// <summary>
    /// Получить плоский список всех деталей с итоговым количеством (с учетом весов).
    /// </summary>
    /// <param name="productId">ID изделия (по умолчанию 1 - Турбокомпрессор)</param>
    /// <returns>Список деталей с количеством</returns>
    [HttpGet("explosion/{productId:int=1}")]
    public async Task<ActionResult<List<BomTreeDto>>> GetMaterialExplosion(int productId = 1)
    {
        var product = await _context.Item.FindAsync(productId);
        if (product == null)
            return NotFound($"Изделие с ID {productId} не найдено");

        var flatList = new List<BomTreeDto>();
        await BuildFlatList(productId, 1, flatList);

        // Группируем по деталям (на случай если одна деталь входит в разные узлы)
        var result = flatList
            .GroupBy(x => new { x.ItemId, x.ItemName })
            .Select(g => new BomTreeDto
            {
                ItemId = g.Key.ItemId,
                ItemName = g.Key.ItemName,
                TotalQuantity = g.Sum(x => x.TotalQuantity)
            })
            .OrderBy(x => x.ItemId)
            .ToList();

        return Ok(result);
    }

    /// <summary>
    /// Получить структуру в виде вложенных списков (для отладки).
    /// </summary>
    /// <param name="productId">ID изделия</param>
    /// <returns>Структура с уровнями вложенности</returns>
    [HttpGet("structure/{productId:int=1}")]
    public async Task<ActionResult<string>> GetStructureText(int productId = 1)
    {
        var product = await _context.Item.FindAsync(productId);
        if (product == null)
            return NotFound($"Изделие с ID {productId} не найдено");

        var sb = new System.Text.StringBuilder();
        await BuildStructureText(productId, 1, 0, sb);
        return Ok(sb.ToString());
    }

    private async Task<BomTreeDto> BuildTree(int itemId, int quantity, int level)
    {
        var item = await _context.Item.FindAsync(itemId);
        if (item == null) return null!;

        var node = new BomTreeDto
        {
            ItemId = item.ItemId,
            ItemName = item.ItemName,
            Level = level,
            TotalQuantity = quantity,
            Children = new List<BomTreeDto>()
        };

        var children = await _context.BOM
            .Include(b => b.Component)
            .Where(b => b.ParentId == itemId)
            .ToListAsync();

        foreach (var child in children)
        {
            var childNode = await BuildTree(child.ComponentId, child.Quantity, level + 1);
            if (childNode != null)
            {
                // Умножаем количество дочерних элементов на количество родителей
                childNode.TotalQuantity = quantity * child.Quantity;
                node.Children.Add(childNode);
            }
        }

        return node;
    }

    private async Task BuildFlatList(int itemId, int quantity, List<BomTreeDto> flatList)
    {
        var children = await _context.BOM
            .Include(b => b.Component)
            .Where(b => b.ParentId == itemId)
            .ToListAsync();

        foreach (var child in children)
        {
            var childItem = child.Component;

            flatList.Add(new BomTreeDto
            {
                ItemId = childItem.ItemId,
                ItemName = childItem.ItemName,
                TotalQuantity = quantity * child.Quantity
            });

            // Рекурсивно обрабатываем вложенные компоненты
            await BuildFlatList(childItem.ItemId, quantity * child.Quantity, flatList);
        }
    }

    private async Task BuildStructureText(int itemId, int quantity, int level, System.Text.StringBuilder sb)
    {
        var item = await _context.Item.FindAsync(itemId);
        if (item == null) return;

        var indent = new string(' ', level * 2);
        sb.AppendLine($"{indent}├─ {item.ItemName} (ID: {item.ItemId}) [x{quantity}]");

        var children = await _context.BOM
            .Where(b => b.ParentId == itemId)
            .ToListAsync();

        foreach (var child in children)
        {
            await BuildStructureText(child.ComponentId, child.Quantity, level + 1, sb);
        }
    }
}