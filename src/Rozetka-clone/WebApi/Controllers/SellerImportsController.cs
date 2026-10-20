using System.Security.Claims;
using Contracts.Catalog;
using Contracts.Telegram;
using Domain.Entities.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WebApi.Telegram;

namespace WebApi.Controllers;

[ApiController, Route("api/v1/seller/imports"), Authorize(Roles = Roles.Seller)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class SellerImportsController(ProductImportService imports, SellerImportQueue queue) : ControllerBase
{
    private Guid UserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet("options")]
    public Task<SellerImportOptionsDto> Options(CancellationToken ct) => imports.OptionsAsync(UserId, ct);

    [HttpGet("queue")]
    public async Task<IReadOnlyList<ImportJobDto>> Jobs(CancellationToken ct)
    { await imports.EnsureSellerAsync(UserId, ct); return await queue.ListAsync(UserId, ct); }

    [HttpPost("queue"), EnableRateLimiting("telegram-import")]
    public Task<IReadOnlyList<ImportJobDto>> Add(QueueImportsRequest request, CancellationToken ct) => queue.AddAsync(UserId, request, ct);

    [HttpPost("queue/{id:guid}/retry"), EnableRateLimiting("telegram-import")]
    public async Task<IActionResult> Retry(Guid id, CancellationToken ct)
    { await queue.RetryAsync(UserId, id, ct); return NoContent(); }

    [HttpGet("{id:guid}")]
    public Task<ImportPreviewDto> Preview(Guid id, CancellationToken ct) => imports.GetAsync(UserId, id, ct);

    [HttpPost("{id:guid}/brand"), EnableRateLimiting("telegram-sensitive")]
    public Task<CatalogLookupDto> Brand(Guid id, CreateImportBrandRequest request, CancellationToken ct) => imports.CreateBrandAsync(UserId, id, request.Name, ct);

    [HttpPost("{id:guid}/submit"), EnableRateLimiting("telegram-import")]
    public async Task<IActionResult> Submit(Guid id, SubmitImportRequest request, CancellationToken ct) => Ok(new { productId = await imports.SubmitAsync(UserId, id, request, ct) });
}
