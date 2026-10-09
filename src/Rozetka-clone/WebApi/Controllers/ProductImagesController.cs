using System.Security.Claims;
using Application.Abstractions;
using Application.ProductImages;
using Domain.Entities.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace WebApi.Controllers
{
    [ApiController]
    [Authorize(Roles = $"{Roles.Administrator},{Roles.Manager},{Roles.Moderator},{Roles.Seller}")]
    [Route("api/v1/products/{productId:guid}/images")]
    public sealed class ProductImagesController : ControllerBase
    {
        private const long MaxImageBytes = 5 * 1024 * 1024;
        private readonly IProductImageService _productImageService;
        private readonly IWebHostEnvironment _environment;
        private readonly IApplicationDbContext _dbContext;

        public ProductImagesController(
            IProductImageService productImageService,
            IWebHostEnvironment environment,
            IApplicationDbContext dbContext
        )
        {
            _productImageService = productImageService;
            _environment = environment;
            _dbContext = dbContext;
        }

        [HttpGet]
        public async Task<ActionResult<IReadOnlyList<ProductImageDto>>> GetAll(
            Guid productId,
            CancellationToken cancellationToken
        )
        {
            if (!await CanReadProductAsync(
                productId,
                cancellationToken
            ))
            {
                return NotFound();
            }

            var images = await _productImageService.GetByProductIdAsync(
                productId,
                cancellationToken
            );

            return Ok(images);
        }

        [HttpGet("{imageId:guid}")]
        public async Task<ActionResult<ProductImageDto>> GetById(
            Guid productId,
            Guid imageId,
            CancellationToken cancellationToken
        )
        {
            if (!await CanReadProductAsync(
                productId,
                cancellationToken
            ))
            {
                return NotFound();
            }

            var image = await _productImageService.GetByIdAsync(
                productId,
                imageId,
                cancellationToken
            );

            if (image is null)
            {
                return NotFound();
            }

            return Ok(image);
        }

        [HttpPost]
        [Authorize(Roles = $"{Roles.Administrator},{Roles.Manager},{Roles.Seller}")]
        public async Task<ActionResult<ProductImageDto>> Create(
            Guid productId,
            [FromBody] CreateProductImageRequest request,
            CancellationToken cancellationToken
        )
        {
            if (!await CanWriteProductAsync(
                productId,
                cancellationToken
            ))
            {
                return NotFound();
            }

            var image = await _productImageService.CreateAsync(
                productId,
                request,
                cancellationToken
            );

            return CreatedAtAction(
                nameof(GetById),
                new
                {
                    productId,
                    imageId = image.Id
                },
                image
            );
        }

        [HttpPost("upload")]
        [Authorize(Roles = $"{Roles.Administrator},{Roles.Manager},{Roles.Seller}")]
        [RequestSizeLimit(MaxImageBytes + 1024 * 1024)]
        public async Task<ActionResult<ProductImageDto>> Upload(
            Guid productId,
            [FromForm] IFormFile file,
            [FromForm] bool isMain,
            [FromForm] int sortOrder,
            CancellationToken cancellationToken
        )
        {
            if (!await CanWriteProductAsync(
                productId,
                cancellationToken
            ))
            {
                return NotFound();
            }

            if (file.Length is <= 0 or > MaxImageBytes)
            {
                return BadRequest("Image size must be between 1 byte and 5 MB.");
            }

            await using var source = file.OpenReadStream();
            using var buffer = new MemoryStream();
            await source.CopyToAsync(
                buffer,
                cancellationToken
            );
            var bytes = buffer.ToArray();
            var imageType = DetectImageType(bytes);
            if (imageType is null)
            {
                return BadRequest("Only JPG, PNG, WEBP and GIF images are supported.");
            }

            var webRoot = _environment.WebRootPath ?? Path.Combine(
                _environment.ContentRootPath,
                "wwwroot"
            );
            var relativeDirectory = Path.Combine(
                "uploads",
                "products",
                productId.ToString("N")
            );
            var directory = Path.Combine(
                webRoot,
                relativeDirectory
            );
            Directory.CreateDirectory(directory);
            var fileName = $"{Guid.NewGuid():N}{imageType.Value.Extension}";
            var absolutePath = Path.Combine(
                directory,
                fileName
            );
            await System.IO.File.WriteAllBytesAsync(
                absolutePath,
                bytes,
                cancellationToken
            );

            try
            {
                var imageUrl = $"/uploads/products/{productId:N}/{fileName}";
                var image = await _productImageService.CreateAsync(
                    productId,
                    new CreateProductImageRequest
                    {
                        ImageUrl = imageUrl,
                        AltText = Path.GetFileNameWithoutExtension(file.FileName),
                        IsMain = isMain,
                        SortOrder = Math.Max(
                            0,
                            sortOrder
                        ),
                    },
                    cancellationToken
                );

                return CreatedAtAction(
                    nameof(GetById),
                    new
                    {
                        productId,
                        imageId = image.Id
                    },
                    image
                );
            }
            catch
            {
                System.IO.File.Delete(absolutePath);
                throw;
            }
        }

        [HttpPatch("{imageId:guid}")]
        [Authorize(Roles = $"{Roles.Administrator},{Roles.Manager},{Roles.Seller}")]
        public async Task<ActionResult<ProductImageDto>> Update(
            Guid productId,
            Guid imageId,
            [FromBody] UpdateProductImageRequest request,
            CancellationToken cancellationToken
        )
        {
            if (!await CanWriteProductAsync(
                productId,
                cancellationToken
            ))
            {
                return NotFound();
            }

            var image = await _productImageService.UpdateAsync(
                productId,
                imageId,
                request,
                cancellationToken
            );

            if (image is null)
            {
                return NotFound();
            }

            return Ok(image);
        }

        [HttpDelete("{imageId:guid}")]
        [Authorize(Roles = $"{Roles.Administrator},{Roles.Manager},{Roles.Seller}")]
        public async Task<IActionResult> Delete(
            Guid productId,
            Guid imageId,
            CancellationToken cancellationToken
        )
        {
            if (!await CanWriteProductAsync(
                productId,
                cancellationToken
            ))
            {
                return NotFound();
            }

            var image = await _productImageService.GetByIdAsync(
                productId,
                imageId,
                cancellationToken
            );
            var deleted = await _productImageService.DeleteAsync(
                productId,
                imageId,
                cancellationToken
            );

            if (!deleted)
            {
                return NotFound();
            }

            if (image is not null)
            {
                DeleteUploadedFile(
                    productId,
                    image.ImageUrl
                );
            }

            return NoContent();
        }

        private Task<bool> CanReadProductAsync(
            Guid productId,
            CancellationToken ct
        )
        {
            return User.IsInRole(Roles.Seller)
                ? OwnsProductAsync(
                    productId,
                    ct
                )
                : Task.FromResult(true);
        }

        private Task<bool> CanWriteProductAsync(
            Guid productId,
            CancellationToken ct
        )
        {
            return User.IsInRole(Roles.Moderator)
                ? Task.FromResult(false)
                : CanReadProductAsync(
                    productId,
                    ct
                );
        }

        private async Task<bool> OwnsProductAsync(
            Guid productId,
            CancellationToken ct
        )
        {
            if (!Guid.TryParse(
                User.FindFirstValue(ClaimTypes.NameIdentifier),
                out var userId
            ))
            {
                return false;
            }

            return await _dbContext.Products.AnyAsync(
                product =>
                    product.Id == productId
                    && _dbContext.Stores.Any(
                        store =>
                            store.Id == product.StoreId
                            && _dbContext.Sellers.Any(
                                seller =>
                                    seller.Id == store.SellerId
                                    && seller.UserId == userId
                            )
                    ),
                ct
            );
        }

        private void DeleteUploadedFile(
            Guid productId,
            string imageUrl
        )
        {
            var expectedPrefix = $"/uploads/products/{productId:N}/";
            if (!imageUrl.StartsWith(
                expectedPrefix,
                StringComparison.OrdinalIgnoreCase
            ))
            {
                return;
            }

            var webRoot = _environment.WebRootPath ?? Path.Combine(
                _environment.ContentRootPath,
                "wwwroot"
            );
            var productRoot = Path.GetFullPath(
                Path.Combine(
                    webRoot,
                    "uploads",
                    "products",
                    productId.ToString("N")
                )
            );
            var absolutePath = Path.GetFullPath(
                Path.Combine(
                    webRoot,
                    imageUrl
                        .TrimStart('/')
                        .Replace(
                            '/',
                            Path.DirectorySeparatorChar
                        )
                )
            );
            if (absolutePath.StartsWith(
                productRoot + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase
            ))
            {
                System.IO.File.Delete(absolutePath);
            }
        }

        private static (string Extension, string ContentType)? DetectImageType(
            byte[] bytes
        )
        {
            if (
                bytes.Length >= 8
                && bytes
                    .AsSpan(
                        0,
                        8
                    )
                    .SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })
            )
            {
                return (".png", "image/png");
            }

            if (bytes.Length >= 3
                && bytes[0] == 0xFF
                && bytes[1] == 0xD8
                && bytes[2] == 0xFF)
            {
                return (".jpg", "image/jpeg");
            }

            if (
                bytes.Length >= 12
                && bytes
                    .AsSpan(
                        0,
                        4
                    )
                    .SequenceEqual("RIFF"u8)
                && bytes
                    .AsSpan(
                        8,
                        4
                    )
                    .SequenceEqual("WEBP"u8)
            )
            {
                return (".webp", "image/webp");
            }

            if (
                bytes.Length >= 6
                && (
                    bytes
                        .AsSpan(
                            0,
                            6
                        )
                        .SequenceEqual("GIF87a"u8)
                    || bytes
                        .AsSpan(
                            0,
                            6
                        )
                        .SequenceEqual("GIF89a"u8)
                )
            )
            {
                return (".gif", "image/gif");
            }

            return null;
        }
    }
}
