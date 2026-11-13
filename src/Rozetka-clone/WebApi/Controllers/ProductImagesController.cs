using System.Security.Claims;
using Application.Abstractions;
using Application.ProductImages;
using Domain.Entities.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApi.Requests;

namespace WebApi.Controllers
{
    [ApiController]
    [Authorize(AuthenticationSchemes = "Bearer," + WebApi.Telegram.TelegramAuthenticationHandler.SchemeName, Roles = $"{Roles.Administrator},{Roles.Manager},{Roles.Moderator},{Roles.Seller}")]
    [Route("api/v1/products/{productId:guid}/images")]
    public sealed class ProductImagesController : ControllerBase
    {
        private const long MaxImageBytes = 5 * 1024 * 1024;

        private readonly IProductImageService _productImageService;

        private readonly IProductMediaStorage _media;

        private readonly IApplicationDbContext _dbContext;

        public ProductImagesController(
            IProductImageService productImageService,
            IProductMediaStorage media,
            IApplicationDbContext dbContext
        )
        {
            _productImageService = productImageService;
            _media = media;
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


        [HttpGet("changes")]
        public async Task<ActionResult<IReadOnlyList<ProductImageChangeDto>>> GetChanges(
            Guid productId,
            CancellationToken cancellationToken
        )
        {
            if (!await CanReadProductAsync(productId, cancellationToken))
            {
                return NotFound();
            }

            var changes = await _productImageService.GetChangesAsync(
                productId,
                cancellationToken
            );

            return Ok(changes);
        }


        [HttpPost("changes/{changeId:guid}/reject")]
        [Authorize(Roles = $"{Roles.Administrator},{Roles.Manager},{Roles.Moderator}")]
        public async Task<ActionResult<ProductImageChangeDto>> RejectChange(
            Guid productId,
            Guid changeId,
            [FromBody] RejectProductImageChangeRequest request,
            CancellationToken cancellationToken
        )
        {
            var change = await _productImageService.RejectChangeAsync(
                productId,
                changeId,
                request.Reason,
                cancellationToken
            );

            if (change is null)
            {
                return NotFound();
            }

            return Ok(change);
        }


        [HttpPost("changes/{changeId:guid}/approve")]
        [Authorize(Roles = $"{Roles.Administrator},{Roles.Manager},{Roles.Moderator}")]
        public async Task<ActionResult<ProductImageChangeDto>> ApproveChange(
            Guid productId,
            Guid changeId,
            CancellationToken cancellationToken
        )
        {
            var change = await _productImageService.ApproveChangeAsync(
                productId,
                changeId,
                cancellationToken
            );

            if (change is null)
            {
                return NotFound();
            }

            return Ok(change);
        }


        [HttpPost]
        [Authorize(Roles = $"{Roles.Administrator},{Roles.Manager},{Roles.Seller}")]
        public async Task<ActionResult<ProductImageChangeDto>> Create(
            Guid productId,
            [FromBody] CreateProductImageRequest request,
            CancellationToken cancellationToken
        )
        {
            if (!await CanWriteProductAsync(productId, cancellationToken))
            {
                return NotFound();
            }

            var change = await _productImageService.SubmitCreateAsync(
                productId,
                request,
                cancellationToken
            );

            return Accepted(change);
        }


        [HttpPost("upload")]
        [Authorize(Roles = $"{Roles.Administrator},{Roles.Manager},{Roles.Seller}")]
        [RequestSizeLimit(MaxImageBytes + 1024 * 1024)]
        public async Task<ActionResult<ProductImageChangeDto>> Upload(
            Guid productId,
            [FromForm] UploadProductImageRequest request,
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

            var file = request.File;

            if (file.Length is <= 0 or > MaxImageBytes)
            {
                return BadRequest(
                    "Image size must be between 1 byte and 5 MB."
                );
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
                return BadRequest(
                    "Only JPG, PNG, WEBP and GIF images are supported."
                );
            }

            var fileName =
                $"{Guid.NewGuid():N}{imageType.Value.Extension}";
            await _media.SaveAsync(productId, fileName, bytes, cancellationToken);

            try
            {
                var imageUrl =
                    $"/uploads/products/{productId:N}/{fileName}";


                var change = await _productImageService.SubmitCreateAsync(
                    productId,
                    new CreateProductImageRequest
                    {
                        ImageUrl = imageUrl,
                        AltText = Path.GetFileNameWithoutExtension(
                            file.FileName
                        ),
                        IsMain = request.IsMain,
                        SortOrder = Math.Max(0, request.SortOrder)
                    },
                    cancellationToken
                );

                return Accepted(change);

            }
            catch
            {
                await _media.DeleteAsync(productId, fileName, CancellationToken.None);
                throw;
            }
        }


        [HttpPatch("{imageId:guid}")]
        [Authorize(Roles = $"{Roles.Administrator},{Roles.Manager},{Roles.Seller}")]
        public async Task<ActionResult<ProductImageChangeDto>> Update(
            Guid productId,
            Guid imageId,
            [FromBody] UpdateProductImageRequest request,
            CancellationToken cancellationToken
        )
        {
            if (!await CanWriteProductAsync(productId, cancellationToken))
            {
                return NotFound();
            }

            var change = await _productImageService.SubmitUpdateAsync(
                productId,
                imageId,
                request,
                cancellationToken
            );

            if (change is null)
            {
                return NotFound();
            }

            return Accepted(change);
        }

        [HttpDelete("{imageId:guid}")]
        [Authorize(Roles = $"{Roles.Administrator},{Roles.Manager},{Roles.Seller}")]
        public async Task<ActionResult<ProductImageChangeDto>> Delete(
            Guid productId,
            Guid imageId,
            CancellationToken cancellationToken
        )
        {
            if (!await CanWriteProductAsync(productId, cancellationToken))
            {
                return NotFound();
            }

            var change = await _productImageService.SubmitDeleteAsync(
                productId,
                imageId,
                cancellationToken
            );

            if (change is null)
            {
                return NotFound();
            }

            return Accepted(change);
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
                                    && seller.Status == Domain.Enums.SellerStatus.ACTIVE
                            )
                    ),
                ct
            );
        }

        private async Task DeleteUploadedFile(
            Guid productId,
            string imageUrl,
            CancellationToken ct
        )
        {
            var expectedPrefix =
                $"/uploads/products/{productId:N}/";

            if (!imageUrl.StartsWith(
                    expectedPrefix,
                    StringComparison.OrdinalIgnoreCase
                ))
            {
                return;
            }

            var fileName = imageUrl[expectedPrefix.Length..];

            if (!fileName.Contains('/') && !fileName.Contains('\\'))
                await _media.DeleteAsync(productId, fileName, ct);
        }

        private static (string Extension, string ContentType)? DetectImageType(
            byte[] bytes
        )
        {
            if (
                bytes.Length >= 8
                && bytes
                    .AsSpan(0, 8)
                    .SequenceEqual(
                        new byte[]
                        {
                            0x89, 0x50, 0x4E, 0x47,
                            0x0D, 0x0A, 0x1A, 0x0A
                        }
                    )
            )
            {
                return (".png", "image/png");
            }

            if (
                bytes.Length >= 3
                && bytes[0] == 0xFF
                && bytes[1] == 0xD8
                && bytes[2] == 0xFF
            )
            {
                return (".jpg", "image/jpeg");
            }

            if (
                bytes.Length >= 12
                && bytes
                    .AsSpan(0, 4)
                    .SequenceEqual("RIFF"u8)
                && bytes
                    .AsSpan(8, 4)
                    .SequenceEqual("WEBP"u8)
            )
            {
                return (".webp", "image/webp");
            }

            if (
                bytes.Length >= 6
                && (
                    bytes
                        .AsSpan(0, 6)
                        .SequenceEqual("GIF87a"u8)
                    || bytes
                        .AsSpan(0, 6)
                        .SequenceEqual("GIF89a"u8)
                )
            )
            {
                return (".gif", "image/gif");
            }

            return null;
        }
        
    }

    public sealed class RejectProductImageChangeRequest
    {
        public required string Reason
        {
            get; init;
        }
    }
}
