
using Application.Abstractions;
using Application.ProductImages;
using Domain.Entities.Product;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace WebApi.Tests
{
    public sealed class ProductImageModerationTests
    {

        private sealed class StubTransactionManager : IApplicationTransactionManager
        {
            public Task<IApplicationTransaction> BeginAsync(
                CancellationToken cancellationToken = default)
            {
                return Task.FromResult<IApplicationTransaction>(new StubTransaction());
            }

            public Task<IApplicationTransaction> BeginForProductAsync(
                Guid productId,
                CancellationToken cancellationToken = default)
            {
                return Task.FromResult<IApplicationTransaction>(new StubTransaction());
            }

            private sealed class StubTransaction : IApplicationTransaction
            {
                public Task CommitAsync(
                    CancellationToken cancellationToken = default)
                {
                    return Task.CompletedTask;
                }

                public ValueTask DisposeAsync()
                {
                    return ValueTask.CompletedTask;
                }
            }
        }

        [Fact]
        public async Task SubmitUpdate_KeepsApprovedImageUntilModeration()
        {
            await using var db = new ApplicationDbContext(
                new DbContextOptionsBuilder<ApplicationDbContext>()
                    .UseInMemoryDatabase(Guid.NewGuid().ToString())
                    .Options
            );

            var productId = Guid.NewGuid();
            var imageId = Guid.NewGuid();

            db.Products.Add(new Product
            {
                Id = productId,
                Name = "Test product",
                Slug = "test-product",
                UpdatedAt = DateTime.UtcNow
            });

            db.ProductImages.Add(new ProductImage
            {
                Id = imageId,
                ProductId = productId,
                ImageUrl = $"/uploads/products/{productId:N}/old.png",
                AltText = "Old photo",
                SortOrder = 0,
                IsMain = true
            });

            await db.SaveChangesAsync();

            var service = new ProductImageService(
                db,
                new StubTransactionManager()
            );

            var newImageUrl = $"/uploads/products/{productId:N}/{Guid.NewGuid():N}.png";

            var change = await service.SubmitUpdateAsync(
                productId,
                imageId,
                new UpdateProductImageRequest
                {
                    ImageUrl = newImageUrl,
                    AltText = "New photo"
                }
            );

            Assert.NotNull(change);
            Assert.Equal(ProductImageChangeStatus.Pending, change.Status);
            Assert.Equal(ProductImageChangeType.Update, change.ChangeType);
            Assert.Equal(newImageUrl, change.ImageUrl);

            var approved = await db.ProductImages
                .AsNoTracking()
                .SingleAsync(x => x.Id == imageId);

            Assert.Equal(
                $"/uploads/products/{productId:N}/old.png",
                approved.ImageUrl
            );
            Assert.Equal("Old photo", approved.AltText);

            var pending = await db.ProductImageChanges.SingleAsync();

            Assert.Equal(ProductImageChangeStatus.Pending, pending.Status);
            Assert.Equal(imageId, pending.ImageId);
        }


        [Fact]
        public async Task ApproveUpdate_PublishesNewImageOnlyAfterApproval()
        {
            await using var db = new ApplicationDbContext(
                new DbContextOptionsBuilder<ApplicationDbContext>()
                    .UseInMemoryDatabase(Guid.NewGuid().ToString())
                    .Options
            );

            var productId = Guid.NewGuid();
            var imageId = Guid.NewGuid();
            var oldImageUrl = $"/uploads/products/{productId:N}/{Guid.NewGuid():N}.png";
            var newImageUrl = $"/uploads/products/{productId:N}/{Guid.NewGuid():N}.png";

            db.Products.Add(new Product
            {
                Id = productId,
                Name = "Test product",
                Slug = "test-product",
                UpdatedAt = DateTime.UtcNow
            });

            db.ProductImages.Add(new ProductImage
            {
                Id = imageId,
                ProductId = productId,
                ImageUrl = oldImageUrl,
                AltText = "Old photo",
                SortOrder = 0,
                IsMain = true
            });

            await db.SaveChangesAsync();

            var service = new ProductImageService(
                db,
                new StubTransactionManager()
            );

            var change = await service.SubmitUpdateAsync(
                productId,
                imageId,
                new UpdateProductImageRequest
                {
                    ImageUrl = newImageUrl,
                    AltText = "New photo"
                }
            );

            Assert.NotNull(change);
            Assert.Equal(ProductImageChangeStatus.Pending, change.Status);

            var beforeApproval = await service.GetByIdAsync(productId, imageId);

            Assert.NotNull(beforeApproval);
            Assert.Equal(oldImageUrl, beforeApproval.ImageUrl);
            Assert.Equal("Old photo", beforeApproval.AltText);

            var approved = await service.ApproveChangeAsync(
                productId,
                change.Id
            );

            Assert.NotNull(approved);
            Assert.Equal(ProductImageChangeStatus.Approved, approved.Status);
            Assert.NotNull(approved.ReviewedAt);

            var afterApproval = await service.GetByIdAsync(productId, imageId);

            Assert.NotNull(afterApproval);
            Assert.Equal(newImageUrl, afterApproval.ImageUrl);
            Assert.Equal("New photo", afterApproval.AltText);

            var savedChange = await db.ProductImageChanges
                .AsNoTracking()
                .SingleAsync(x => x.Id == change.Id);

            Assert.Equal(ProductImageChangeStatus.Approved, savedChange.Status);
        }


        [Fact]
        public async Task RejectUpdate_KeepsApprovedImageAndSavesReason()
        {
            await using var db = new ApplicationDbContext(
                new DbContextOptionsBuilder<ApplicationDbContext>()
                    .UseInMemoryDatabase(Guid.NewGuid().ToString())
                    .Options
            );

            var productId = Guid.NewGuid();
            var imageId = Guid.NewGuid();
            var oldImageUrl = $"/uploads/products/{productId:N}/{Guid.NewGuid():N}.png";
            var newImageUrl = $"/uploads/products/{productId:N}/{Guid.NewGuid():N}.png";

            db.Products.Add(new Product
            {
                Id = productId,
                Name = "Test product",
                Slug = "test-product",
                UpdatedAt = DateTime.UtcNow
            });

            db.ProductImages.Add(new ProductImage
            {
                Id = imageId,
                ProductId = productId,
                ImageUrl = oldImageUrl,
                AltText = "Approved photo",
                SortOrder = 0,
                IsMain = true
            });

            await db.SaveChangesAsync();

            var service = new ProductImageService(
                db,
                new StubTransactionManager()
            );

            var change = await service.SubmitUpdateAsync(
                productId,
                imageId,
                new UpdateProductImageRequest
                {
                    ImageUrl = newImageUrl,
                    AltText = "Unapproved photo"
                }
            );

            Assert.NotNull(change);

            var rejected = await service.RejectChangeAsync(
                productId,
                change.Id,
                "  Photo does not meet quality requirements.  "
            );

            Assert.NotNull(rejected);
            Assert.Equal(ProductImageChangeStatus.Rejected, rejected.Status);
            Assert.Equal(
                "Photo does not meet quality requirements.",
                rejected.RejectionReason
            );
            Assert.NotNull(rejected.ReviewedAt);

            var published = await service.GetByIdAsync(productId, imageId);

            Assert.NotNull(published);
            Assert.Equal(oldImageUrl, published.ImageUrl);
            Assert.Equal("Approved photo", published.AltText);

            var savedChange = await db.ProductImageChanges
                .AsNoTracking()
                .SingleAsync(x => x.Id == change.Id);

            Assert.Equal(ProductImageChangeStatus.Rejected, savedChange.Status);
            Assert.Equal(rejected.RejectionReason, savedChange.RejectionReason);
        }


        [Fact]
        public async Task ApproveUpdate_Twice_RejectsSecondApproval()
        {
            await using var db = new ApplicationDbContext(
                new DbContextOptionsBuilder<ApplicationDbContext>()
                    .UseInMemoryDatabase(Guid.NewGuid().ToString())
                    .Options
            );

            var productId = Guid.NewGuid();
            var imageId = Guid.NewGuid();

            db.Products.Add(new Product
            {
                Id = productId,
                Name = "Test product",
                Slug = "test-product",
                UpdatedAt = DateTime.UtcNow
            });

            db.ProductImages.Add(new ProductImage
            {
                Id = imageId,
                ProductId = productId,
                ImageUrl = $"/uploads/products/{productId:N}/{Guid.NewGuid():N}.png",
                SortOrder = 0,
                IsMain = true
            });

            await db.SaveChangesAsync();

            var service = new ProductImageService(
                db,
                new StubTransactionManager()
            );

            var newImageUrl =
                $"/uploads/products/{productId:N}/{Guid.NewGuid():N}.png";

            var change = await service.SubmitUpdateAsync(
                productId,
                imageId,
                new UpdateProductImageRequest
                {
                    ImageUrl = newImageUrl
                }
            );

            Assert.NotNull(change);

            await service.ApproveChangeAsync(productId, change.Id);

            await Assert.ThrowsAsync<Application.Common.BusinessRuleException>(
                () => service.ApproveChangeAsync(productId, change.Id)
            );

            var published = await service.GetByIdAsync(productId, imageId);

            Assert.NotNull(published);
            Assert.Equal(newImageUrl, published.ImageUrl);
            Assert.Single(await db.ProductImageChanges.ToListAsync());
        }

    }
}
