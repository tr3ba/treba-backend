using Application.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace WebApi.Controllers;

[ApiController]
public sealed class ProductMediaController(IProductMediaStorage media) : ControllerBase
{
    [HttpGet("/uploads/products/{productId:guid}/{fileName}")]
    [AllowAnonymous]
    public async Task<IActionResult> Get(Guid productId, string fileName, CancellationToken ct)
    {
        var file = await media.ReadAsync(productId, fileName, ct);
        if (file is null) return NotFound();
        Response.Headers.CacheControl = "public,max-age=3600";
        return File(file.Bytes, file.ContentType);
    }
}
