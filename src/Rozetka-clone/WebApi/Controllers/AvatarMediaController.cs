using Application.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;
using WebApi.Media;

namespace WebApi.Controllers;

[ApiController]
public sealed class AvatarMediaController(IApplicationDbContext db, IWebHostEnvironment environment) : ControllerBase
{
    [HttpGet("/uploads/avatars/{userId:guid}/{fileName}"), AllowAnonymous]
    public async Task<IActionResult> Get(Guid userId, string fileName, CancellationToken ct)
    {
        if (!Regex.IsMatch(fileName, @"^[a-fA-F0-9]{32}\.(png|jpe?g|webp|gif)\z", RegexOptions.IgnoreCase))
            return NotFound();
        var url = $"/uploads/avatars/{userId:N}/{fileName}";
        if (!await db.UserProfiles.AnyAsync(p => p.UserId == userId && p.AvatarUrl == url, ct))
            return NotFound();
        var root = environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot");
        var path = Path.Combine(root, "uploads", "avatars", userId.ToString("N"), fileName);
        var file = new FileInfo(path);
        if (!file.Exists || file.Length > 5 * 1024 * 1024)
            return NotFound();
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var contentType = extension switch
        {
            ".jpg" or ".jpeg" => "image/jpeg", ".png" => "image/png", ".webp" => "image/webp", _ => "image/gif"
        };
        var safe = SafeImage.Normalize(await System.IO.File.ReadAllBytesAsync(path, ct), extension);
        Response.Headers.CacheControl = "public,max-age=3600";
        return File(safe, contentType);
    }
}
