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
    IApplicationDbContext db
) : ControllerBase
{
    [HttpGet("products")]
    public async Task<PagedResponse<CatalogProductDto>> List(
        int page = 1,
        int size = 20,
        string? search = null,
        string? status = null,
        CancellationToken ct = default
    )
    {
        return await catalog.ListAsync(
            page,
            size,
            search,
            status,
            false,
            ct,
            SellerUserScope()
        );
    }

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
        CancellationToken ct = default
    )
    {
        return catalog.ListAsync(
            page,
            size,
            search,
            null,
            true,
            ct
        );
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
