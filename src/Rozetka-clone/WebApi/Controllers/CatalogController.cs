using System.Security.Claims;
using Application.Abstractions;
using Application.Catalog;
using Contracts.Catalog;
using Contracts.Common;
using Domain.Entities.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace WebApi.Controllers;

[ApiController, Route("api/v1/admin/catalog"), Authorize(Roles = Roles.Administrator)]
public sealed class CatalogController(CatalogService catalog, IApplicationDbContext db) : ControllerBase
{
    [HttpGet("products")]
    public Task<PagedResponse<CatalogProductDto>> List(
    int page = 1,
    int size = 20,
    string? search = null,
    string? status = null,
    CancellationToken ct = default) =>
    catalog.ListAsync(page, size, search, status, false, null, null, ct);

    [HttpGet("stats")]
    public Task<CatalogStatsDto> Stats(CancellationToken ct) => catalog.StatsAsync(ct);

    [HttpGet("moderation/pending-count")]
    public Task<int> PendingCount(CancellationToken ct) => catalog.PendingCountAsync(ct);

    [HttpGet("products/{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) => await catalog.GetAsync(id, ct) is { } product ? Ok(product) : NotFound();

    [HttpGet("categories")]
    public async Task<IReadOnlyList<CatalogLookupDto>> Categories(CancellationToken ct) => await db.Categories.AsNoTracking().Where(c => c.IsActive).OrderBy(c => c.Name).Select(c => new CatalogLookupDto(c.Id, c.Name)).ToListAsync(ct);

    [HttpGet("brands")]
    public async Task<IReadOnlyList<CatalogLookupDto>> Brands(CancellationToken ct) => await db.Brands.AsNoTracking().Where(b => b.IsActive).OrderBy(b => b.Name).Select(b => new CatalogLookupDto(b.Id, b.Name)).ToListAsync(ct);

    [HttpPost("products")]
    public async Task<IActionResult> Create(SaveCatalogProductRequest request, CancellationToken ct)
    {
        var product = await catalog.SaveAsync(null, Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!), request, ct);
        return CreatedAtAction(nameof(Get), new { id = product!.Id }, product);
    }

    [HttpPut("products/{id:guid}")]
    public async Task<IActionResult> Update(Guid id, SaveCatalogProductRequest request, CancellationToken ct) =>
        await catalog.SaveAsync(id, Guid.Empty, request, ct) is { } product ? Ok(product) : NotFound();

    [HttpGet("/api/v1/catalog"), AllowAnonymous]
    public Task<PagedResponse<CatalogProductDto>> Public(
    int page = 1,
    int size = 20,
    string? search = null,
    Guid? categoryId = null,
    string? sort = null,
    CancellationToken ct = default) =>
    catalog.ListAsync(page, size, search, null, true, categoryId, sort, ct);


    [HttpGet("/api/v1/catalog/{slug}"), AllowAnonymous]
    public async Task<IActionResult> PublicBySlug(
    string slug,
    CancellationToken ct)
    {
        var product = await catalog.GetBySlugAsync(slug, true, ct);

        return product is not null
            ? Ok(product)
            : NotFound();
    }
}
