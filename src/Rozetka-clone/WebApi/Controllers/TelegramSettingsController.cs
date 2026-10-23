using Contracts.Telegram;
using Domain.Entities.Telegram;
using Domain.Entities.Users;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace WebApi.Controllers;

[ApiController, Route("api/v1/admin/telegram"), Authorize(Roles = Roles.Administrator)]
public sealed class TelegramSettingsController(ApplicationDbContext db) : ControllerBase
{
    [HttpGet("mappings")]
    public async Task<IReadOnlyList<CategoryMappingDto>> Mappings(CancellationToken ct) => await db.RozetkaCategoryMappings.AsNoTracking()
        .Join(db.Categories, m => m.CategoryId, c => c.Id, (m,c) => new { Mapping = m, Category = c })
        .OrderBy(row => row.Mapping.ExternalKey)
        .Select(row => new CategoryMappingDto(row.Mapping.Id, row.Mapping.ExternalKey, row.Mapping.CategoryId, row.Category.Name)).ToListAsync(ct);
    [HttpPut("mappings")]
    public async Task<IActionResult> SaveMapping(CategoryMappingInput request, CancellationToken ct)
    {
        if (!await db.Categories.AnyAsync(c => c.Id == request.CategoryId && c.IsActive, ct)) return BadRequest(new { message = "Виберіть активну категорію Treba." });
        var mapping = await db.RozetkaCategoryMappings.SingleOrDefaultAsync(m => m.ExternalKey == request.ExternalKey, ct);
        if (mapping is null) { mapping = new() { ExternalKey = request.ExternalKey }; db.RozetkaCategoryMappings.Add(mapping); }
        mapping.CategoryId = request.CategoryId; await db.SaveChangesAsync(ct); return NoContent();
    }
    [HttpGet("sellers/{sellerId:guid}/categories")]
    public async Task<IActionResult> SellerCategories(Guid sellerId, CancellationToken ct)
    {
        if (!await db.Sellers.AnyAsync(s => s.Id == sellerId, ct)) return NotFound();
        return Ok(await db.SellerCategoryGrants.AsNoTracking().Where(g => g.SellerId == sellerId)
            .Join(db.Categories, g => g.CategoryId, c => c.Id, (g,c) => new { Grant = g, Category = c })
            .OrderBy(row => row.Category.Name)
            .Select(row => new SellerCategoryGrantDto(row.Category.Id, row.Category.Name, row.Grant.IncludeDescendants)).ToListAsync(ct));
    }
    [HttpPut("sellers/{sellerId:guid}/categories")]
    public async Task<IActionResult> SetSellerCategories(Guid sellerId, UpdateSellerCategoriesRequest request, CancellationToken ct)
    {
        if (!await db.Sellers.AnyAsync(s => s.Id == sellerId, ct)) return NotFound();
        if (request.Grants.Any(g => g is null || g.CategoryId == Guid.Empty) || request.Grants.Select(g => g.CategoryId).Distinct().Count() != request.Grants.Count)
            return BadRequest(new { message = "Кожну категорію можна вибрати лише один раз." });
        var ids = request.Grants.Select(g => g.CategoryId).ToArray();
        if (await db.Categories.CountAsync(c => ids.Contains(c.Id) && c.IsActive, ct) != ids.Length) return BadRequest();
        var previous = await db.SellerCategoryGrants.Where(g => g.SellerId == sellerId).ToListAsync(ct);
        foreach (var old in previous.Where(g => !ids.Contains(g.CategoryId))) db.SellerCategoryGrants.Remove(old);
        foreach (var input in request.Grants)
        {
            var grant = previous.SingleOrDefault(g => g.CategoryId == input.CategoryId);
            if (grant is null) { grant = new() { SellerId = sellerId, CategoryId = input.CategoryId }; db.SellerCategoryGrants.Add(grant); }
            grant.IncludeDescendants = input.IncludeDescendants;
        }
        await db.SaveChangesAsync(ct); return NoContent();
    }
}
