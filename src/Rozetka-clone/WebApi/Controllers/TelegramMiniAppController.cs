using System.Security.Claims;
using Application.Administration;
using Application.Catalog;
using Application.Products;
using Application.Reviews;
using Contracts.Admin.Reviews;
using Contracts.Catalog;
using Contracts.Telegram;
using Domain.Entities.Users;
using Domain.Enums;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using WebApi.Telegram;

namespace WebApi.Controllers;

[ApiController, Route("api/telegram/mini"), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Authorize(AuthenticationSchemes = TelegramAuthenticationHandler.SchemeName, Roles = "Administrator,Manager,Moderator,Seller")]
public sealed class TelegramMiniAppController(ApplicationDbContext db, CatalogService catalog, IProductService products,
    TelegramGuardService guard, ProductImportService import, SellerCategoryAccess categories, AdminWorkspaceService workspace, IReviewService reviews) : ControllerBase
{
    private Guid UserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private Guid? SellerScope => User.IsInRole(Roles.Seller) ? UserId : null;
    private bool IsSeller => User.IsInRole(Roles.Seller);
    [HttpGet("bootstrap")]
    public async Task<IActionResult> Bootstrap(CancellationToken ct)
    {
        var options = await catalog.FilterOptionsAsync(SellerScope, ct);
        var stores = IsSeller ? await db.Stores.AsNoTracking().Where(s => s.IsActive && db.Sellers.Any(seller => seller.Id == s.SellerId && seller.UserId == UserId))
            .Select(s => new CatalogLookupDto(s.Id, s.Name)).ToListAsync(ct) : [];
        return Ok(new { user = new TelegramUserDto(UserId, User.FindFirstValue("firstName") + " " + User.FindFirstValue("lastName"), User.FindFirstValue(ClaimTypes.Role)!, long.Parse(User.FindFirstValue("telegram_id")!, System.Globalization.CultureInfo.InvariantCulture)),
            stats = await catalog.StatsAsync(ct, SellerScope), categories = options.Categories, brands = options.Brands, stores,
            allowedCategoryIds = IsSeller ? await categories.AllowedIdsAsync(UserId, ct) : [],
            canClassifyImports = IsSeller && await categories.CanClassifyUnmappedImportsAsync(UserId, ct),
            pendingReviews = IsSeller ? 0 : await workspace.PendingReviewsAsync(ct) });
    }
    [HttpGet("products")]
    public async Task<IActionResult> List(int page = 1, string? search = null, string? status = null, CancellationToken ct = default) =>
        Ok(await catalog.ListAsync(page, 12, search, status, false, null, "updated", ct, SellerScope));
    [HttpGet("products/{id:guid}")]
    public async Task<IActionResult> Product(Guid id, CancellationToken ct) => await catalog.GetAsync(id, ct, SellerScope) is { } product ? Ok(product) : NotFound();
    [HttpGet("products/{id:guid}/history")]
    public async Task<IActionResult> History(Guid id, CancellationToken ct) => await catalog.GetAsync(id, ct, SellerScope) is null ? NotFound() : Ok(await catalog.ModerationHistoryAsync(id, ct));
    [HttpGet("attributes")]
    public async Task<IActionResult> Attributes(Guid categoryId, CancellationToken ct) => Ok(await catalog.AttributeDefinitionsAsync(categoryId, ct));
    [HttpPost("products"), Authorize(Roles = Roles.Seller)]
    public async Task<IActionResult> Create(SaveCatalogProductRequest request, CancellationToken ct)
    {
        await categories.EnsureAllowedAsync(UserId, request.CategoryId, ct);
        if (!await OwnsStore(request.StoreId, ct)) return NotFound();
        return Ok(await catalog.SaveAsync(null, request.StoreId, request, ct));
    }
    [HttpPut("products/{id:guid}"), Authorize(Roles = Roles.Seller)]
    public async Task<IActionResult> Save(Guid id, SaveCatalogProductRequest request, CancellationToken ct)
    {
        if (await catalog.GetAsync(id, ct, UserId) is null) return NotFound();
        await categories.EnsureAllowedAsync(UserId, request.CategoryId, ct);
        return Ok(await catalog.SaveAsync(id, Guid.Empty, request, ct));
    }
    [HttpPost("products/{id:guid}/submit"), Authorize(Roles = Roles.Seller)]
    public async Task<IActionResult> Submit(Guid id, CancellationToken ct)
    {
        var product = await catalog.GetAsync(id, ct, UserId); if (product is null) return NotFound();
        await categories.EnsureAllowedAsync(UserId, product.CategoryId, ct);
        await products.SubmitForModerationAsync(id, ct); return NoContent();
    }
    [HttpPost("products/{id:guid}/approve"), Authorize(Roles = Roles.Moderator)]
    public async Task<IActionResult> Approve(Guid id, CancellationToken ct) => await products.ApproveAsync(id, ct) ? NoContent() : NotFound();
    [HttpPost("products/{id:guid}/reject"), Authorize(Roles = Roles.Moderator)]
    public async Task<IActionResult> Reject(Guid id, RejectProductRequest request, CancellationToken ct) => await products.RejectAsync(id, request.Reason, ct) ? NoContent() : NotFound();
    [HttpGet("reviews"), Authorize(Roles = Roles.Moderator)]
    public async Task<IActionResult> Reviews(int page = 1, string? status = "PENDING", string? search = null, CancellationToken ct = default) => Ok(await workspace.ReviewsAsync(page, 12, status, search, ct));
    [HttpPut("reviews/{id:guid}"), Authorize(Roles = Roles.Moderator)]
    public async Task<IActionResult> Review(Guid id, ModerateReviewRequest request, CancellationToken ct)
    {
        var status = AdminWorkspaceService.ParseReviewStatus(request.Status);
        if (status is not (ReviewStatus.PUBLISHED or ReviewStatus.REJECTED)) return BadRequest();
        await reviews.ModerateReviewAsync(id, status, ct); return NoContent();
    }
    [HttpPost("guard/preview")]
    public Task<GuardPreviewDto> GuardPreview(GuardScanRequest request, CancellationToken ct) => guard.PreviewAsync(request.QrPayload, UserId, ct);
    [HttpPost("guard/approve"), EnableRateLimiting("telegram-sensitive")]
    public async Task<IActionResult> GuardApprove(GuardScanRequest request, CancellationToken ct)
    { await guard.ApproveAsync(request.QrPayload, UserId, long.Parse(User.FindFirstValue("telegram_id")!, System.Globalization.CultureInfo.InvariantCulture), ct); return NoContent(); }
    [HttpPost("imports"), Authorize(Roles = Roles.Seller), EnableRateLimiting("telegram-import")]
    public Task<ImportPreviewDto> Import(ImportUrlRequest request, CancellationToken ct) => import.PreviewAsync(UserId, request.Url, ct, request.CategoryId);
    [HttpGet("imports/queue"), Authorize(Roles = Roles.Seller)]
    public Task<IReadOnlyList<ImportJobDto>> ImportQueue([FromServices] SellerImportQueue queue, CancellationToken ct) => queue.ListAsync(UserId, ct);
    [HttpPost("imports/queue"), Authorize(Roles = Roles.Seller), EnableRateLimiting("telegram-import")]
    public Task<IReadOnlyList<ImportJobDto>> QueueImports(QueueImportsRequest request, [FromServices] SellerImportQueue queue, CancellationToken ct) => queue.AddAsync(UserId, request, ct);
    [HttpPost("imports/queue/{id:guid}/retry"), Authorize(Roles = Roles.Seller), EnableRateLimiting("telegram-import")]
    public async Task<IActionResult> RetryImport(Guid id, [FromServices] SellerImportQueue queue, CancellationToken ct) { await queue.RetryAsync(UserId, id, ct); return NoContent(); }
    [HttpGet("duplicates"), Authorize(Roles = Roles.Seller)]
    public Task<IReadOnlyList<ProductDuplicateDto>> Duplicates(Guid brandId, string? manufacturerPartNumber, string? barcode, Guid? excludeId,
        [FromServices] CatalogDuplicateService duplicates, CancellationToken ct) => duplicates.FindAsync(UserId, brandId, manufacturerPartNumber, barcode, excludeId, ct);
    [HttpPost("duplicates"), Authorize(Roles = Roles.Seller)]
    public Task<IReadOnlyList<ProductDuplicateDto>> LookupDuplicates(DuplicateLookupRequest request,
        [FromServices] CatalogDuplicateService duplicates, CancellationToken ct) => duplicates.FindManyAsync(UserId, request.BrandId, request.ManufacturerPartNumber, request.Barcodes, request.ExcludeId, ct);
    [HttpPost("bulk/preview"), Authorize(Roles = Roles.Seller)]
    public Task<BulkCatalogPreviewDto> BulkPreview(BulkCatalogRequest request, [FromServices] BulkCatalogService bulk, CancellationToken ct) => bulk.PreviewAsync(UserId, UserId, request, ct);
    [HttpPost("bulk/apply"), Authorize(Roles = Roles.Seller)]
    public async Task<IActionResult> BulkApply(ApplyBulkCatalogRequest request, [FromServices] BulkCatalogService bulk, CancellationToken ct) => Ok(new { updated = await bulk.ApplyAsync(UserId, UserId, request.Token, ct) });
    [HttpGet("imports/{id:guid}"), Authorize(Roles = Roles.Seller)]
    public Task<ImportPreviewDto> Preview(Guid id, CancellationToken ct) => import.GetAsync(UserId, id, ct);
    [HttpPost("imports/{id:guid}/submit"), Authorize(Roles = Roles.Seller), EnableRateLimiting("telegram-import")]
    public async Task<IActionResult> SubmitImport(Guid id, SubmitImportRequest request, CancellationToken ct) => Ok(new { productId = await import.SubmitAsync(UserId, id, request, ct) });
    private Task<bool> OwnsStore(Guid id, CancellationToken ct) => db.Stores.AnyAsync(s => s.Id == id && s.IsActive && db.Sellers.Any(seller => seller.Id == s.SellerId && seller.UserId == UserId && seller.Status == SellerStatus.ACTIVE), ct);
}
