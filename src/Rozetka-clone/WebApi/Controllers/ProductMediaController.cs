using Application.Abstractions;
using Application.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace WebApi.Controllers;

[ApiController]
public sealed class ProductMediaController(IProductMediaStorage media, IApplicationDbContext db, ICurrentActor actor, WebApi.Telegram.IMarketplaceSourceClient source, WebApi.Media.ProductMediaUrls urls) : ControllerBase
{
    [HttpGet("/uploads/products/{productId:guid}/{fileName}")]
    [AllowAnonymous]
    public async Task<IActionResult> Get(Guid productId, string fileName, CancellationToken ct, [FromQuery] string? access = null)
    {
        var visible = await db.Products.PubliclyVisible(db).AnyAsync(p => p.Id == productId, ct);
        var signedUserId = urls.Validate(productId, fileName, access);
        var userId = signedUserId ?? actor.UserId;
        if (!visible && userId is null && Request.Headers.Authorization.ToString().StartsWith("Tma ", StringComparison.OrdinalIgnoreCase))
        {
            var authentication = await Microsoft.AspNetCore.Authentication.AuthenticationHttpContextExtensions.AuthenticateAsync(
                HttpContext, WebApi.Telegram.TelegramAuthenticationHandler.SchemeName);
            if (authentication.Succeeded)
                userId = Guid.Parse(authentication.Principal!.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
        }
        var user = userId is null ? null : await db.Users.AsNoTracking().Include(u => u.Role)
            .SingleOrDefaultAsync(u => u.Id == userId && !u.IsDeleted && u.Status == Domain.Entities.Users.UserStatus.Active, ct);
        var staff = user?.Role?.Name is "Administrator" or "Manager" or "Moderator";
        var owner = user?.Role?.Name == "Seller" && await db.Products.AnyAsync(p => p.Id == productId
            && db.Stores.Any(s => s.Id == p.StoreId && db.Sellers.Any(seller => seller.Id == s.SellerId
                && seller.UserId == userId && seller.Status == Domain.Enums.SellerStatus.ACTIVE)), ct);
        if (!visible && !staff && !owner)
            return NotFound();

        ProductMediaFile? file;
        if (fileName.StartsWith("external-", StringComparison.Ordinal) && Guid.TryParseExact(fileName[9..], "N", out var imageId))
        {
            var image = await db.ProductImages.AsNoTracking().SingleOrDefaultAsync(i => i.ProductId == productId && i.Id == imageId, ct);
            if (image is null || !image.ImageUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return NotFound();
            var bytes = await source.GetImageAsync(WebApi.Telegram.MarketplaceSourceClient.ImageUri(image.ImageUrl, true), ct);
            file = new ProductMediaFile(WebApi.Media.SafeImage.Normalize(bytes, ".png"), "image/png");
        }
        else
            file = await media.ReadAsync(productId, fileName, ct);

        if (file is null)
            return NotFound();
        Response.Headers.CacheControl = "private,no-store";
        return File(file.Bytes, file.ContentType);
    }
}
