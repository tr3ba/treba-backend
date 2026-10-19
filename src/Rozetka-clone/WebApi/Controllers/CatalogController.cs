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

[ApiController]
[Route("api/v1/admin/catalog")]
[Authorize(Roles = $"{Roles.Administrator},{Roles.Manager},{Roles.Moderator},{Roles.Seller}")]
public sealed class CatalogController(
    CatalogService catalog,
    IApplicationDbContext db,
    Microsoft.Extensions.Options.IOptions<WebApi.Telegram.TelegramOptions> telegram
) : ControllerBase
{
    [HttpGet("products")]
    public async Task<PagedResponse<CatalogProductDto>> List(
        int page = 1,
        int size = 20,
        string? search = null,
        string? status = null,
        Guid? categoryId = null,
        Guid? brandId = null,
        Guid? sellerId = null,
        decimal? minPrice = null,
        decimal? maxPrice = null,
        string? stock = null,
        string? sort = null,
        CancellationToken ct = default
    )
    {
        return await catalog.ListAsync(
            page,
            size,
            search,
            status,
            false,
            categoryId,
            sort,
            ct,
            SellerUserScope(),
            new CatalogFilters { BrandId = brandId, SellerId = sellerId, MinPrice = minPrice, MaxPrice = maxPrice, Stock = stock }
        );
    }

    [HttpGet("filter-options")]
    public Task<CatalogFilterOptionsDto> FilterOptions(CancellationToken ct) => catalog.FilterOptionsAsync(SellerUserScope(), ct);

    [HttpGet("attributes")]
    public Task<IReadOnlyList<CatalogAttributeDefinitionDto>> Attributes(Guid categoryId, CancellationToken ct) => catalog.AttributeDefinitionsAsync(categoryId, ct);

    [HttpPost("attributes")]
    [Authorize(Roles = Roles.Administrator)]
    public async Task<IActionResult> CreateAttribute(CreateCatalogAttributeRequest request, [FromServices] Application.Attributes.IAttributeService attributes, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || !await db.Categories.AnyAsync(c => c.Id == request.CategoryId && c.IsActive, ct)) return BadRequest();
        var attribute = await attributes.CreateAsync(new Application.Attributes.CreateAttributeRequest {
            CategoryId = request.CategoryId, Name = request.Name.Trim(), Code = "custom-" + Guid.NewGuid().ToString("N"),
            Type = Domain.Enums.AttributeType.STRING, Unit = request.Unit?.Trim() }, ct);
        return Ok(attribute);
    }

    [HttpGet("products/{id:guid}/moderation-history")]
    public async Task<IActionResult> History(Guid id, CancellationToken ct)
        => await catalog.GetAsync(id, ct, SellerUserScope()) is null ? NotFound() : Ok(await catalog.ModerationHistoryAsync(id, ct));

    [HttpGet("stats")]
    public async Task<CatalogStatsDto> Stats(
        CancellationToken ct
    )
    {
        return await catalog.StatsAsync(
            ct,
            SellerUserScope()
        );
    }

    [HttpGet("moderation/pending-count")]
    [Authorize(Roles = $"{Roles.Administrator},{Roles.Manager},{Roles.Moderator}")]
    public Task<int> PendingCount(
        CancellationToken ct
    )
    {
        return catalog.PendingCountAsync(ct);
    }

    [HttpGet("products/{id:guid}")]
    public async Task<IActionResult> Get(
        Guid id,
        CancellationToken ct
    )
    {
        return await catalog.GetAsync(
            id,
            ct,
            SellerUserScope()
        ) is { } product
            ? Ok(product)
            : NotFound();
    }

    [HttpGet("categories")]
    public async Task<IReadOnlyList<CatalogLookupDto>> Categories(
        CancellationToken ct
    )
    {
        return await db
            .Categories
            .AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.Name)
            .Select(
                c =>
                    new CatalogLookupDto(
                        c.Id,
                        c.Name
                    )
            )
            .ToListAsync(ct);
    }

    [HttpGet("brands")]
    public async Task<IReadOnlyList<CatalogLookupDto>> Brands(
        CancellationToken ct
    )
    {
        return await db
            .Brands
            .AsNoTracking()
            .Where(b => b.IsActive)
            .OrderBy(b => b.Name)
            .Select(
                b =>
                    new CatalogLookupDto(
                        b.Id,
                        b.Name
                    )
            )
            .ToListAsync(ct);
    }

    [HttpGet("stores")]
    [Authorize(Roles = $"{Roles.Administrator},{Roles.Manager},{Roles.Seller}")]
    public async Task<IReadOnlyList<CatalogLookupDto>> Stores(
        CancellationToken ct
    )
    {
        var sellerUserId = SellerUserScope();

        return await db
            .Stores
            .AsNoTracking()
            .Where(
                store =>
                    store.IsActive
                    && (
                        !sellerUserId.HasValue
                        || db.Sellers.Any(
                            seller =>
                                seller.Id == store.SellerId
                                && seller.UserId == sellerUserId.Value
                        )
                    )
            )
            .OrderBy(store => store.Name)
            .Select(
                store =>
                    new CatalogLookupDto(
                        store.Id,
                        store.Name
                    )
            )
            .ToListAsync(ct);
    }

    [HttpPost("products")]
    [Authorize(Roles = $"{Roles.Administrator},{Roles.Manager},{Roles.Seller}")]
    public async Task<IActionResult> Create(
        SaveCatalogProductRequest request,
        CancellationToken ct
    )
    {
        var sellerUserId = SellerUserScope();

        var storeIsAllowed =
            request.StoreId != Guid.Empty
            && await db.Stores.AnyAsync(
                store =>
                    store.Id == request.StoreId
                    && store.IsActive
                    && (
                        !sellerUserId.HasValue
                        || db.Sellers.Any(
                            seller =>
                                seller.Id == store.SellerId
                                && seller.UserId == sellerUserId.Value
                        )
                    ),
                ct
            );

        if (!storeIsAllowed)
        {
            return BadRequest(
                new
                {
                    message = "Select an active store available to your account."
                }
            );
        }

        if (sellerUserId.HasValue && telegram.Value.Enabled && telegram.Value.EnforceSellerCategories)
            await new SellerCategoryAccess(db).EnsureAllowedAsync(sellerUserId.Value, request.CategoryId, ct);

        var product = await catalog.SaveAsync(
            null,
            request.StoreId,
            request,
            ct
        );

        return CreatedAtAction(
            nameof(Get),
            new
            {
                id = product!.Id
            },
            product
        );
    }

    [HttpPut("products/{id:guid}")]
    [Authorize(Roles = $"{Roles.Administrator},{Roles.Manager},{Roles.Seller}")]
    public async Task<IActionResult> Update(
        Guid id,
        SaveCatalogProductRequest request,
        CancellationToken ct
    )
    {
        if (User.IsInRole(Roles.Seller)
            && await catalog.GetAsync(
                id,
                ct,
                SellerUserScope()
            ) is null)
        {
            return NotFound();
        }

        if (SellerUserScope() is { } sellerId && telegram.Value.Enabled && telegram.Value.EnforceSellerCategories)
            await new SellerCategoryAccess(db).EnsureAllowedAsync(sellerId, request.CategoryId, ct);

        return await catalog.SaveAsync(
            id,
            Guid.Empty,
            request,
            ct
        ) is { } product
            ? Ok(product)
            : NotFound();
    }

    [HttpGet("/api/v1/catalog")]
    [AllowAnonymous]
    public Task<PagedResponse<CatalogProductDto>> Public(
        int page = 1,
        int size = 20,
        string? search = null,
        Guid? categoryId = null,
        string? sort = null,
        CancellationToken ct = default
    )
    {
        return catalog.ListAsync(
            page,
            size,
            search,
            null,
            true,
            categoryId,
            sort,
            ct
        );
    }

    [HttpGet("/api/v1/catalog/{slug}")]
    [AllowAnonymous]
    public async Task<IActionResult> PublicBySlug(
        string slug,
        CancellationToken ct
    )
    {
        var product = await catalog.GetBySlugAsync(
            slug,
            true,
            ct
        );

        return product is not null
            ? Ok(product)
            : NotFound();
    }

    private Guid? SellerUserScope()
    {
        return User.IsInRole(Roles.Seller)
            ? Guid.TryParse(
                User.FindFirstValue(ClaimTypes.NameIdentifier),
                out var userId
            )
                ? userId
                : Guid.Empty
            : null;
    }
}
