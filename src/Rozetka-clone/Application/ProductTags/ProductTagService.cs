using Application.Abstractions;
using Domain.Entities.ProductTag;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text;

namespace Application.ProductTags
{
    public sealed class ProductTagService : IProductTagService
    {
        private readonly IApplicationDbContext _dbContext;

        public ProductTagService(IApplicationDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task<IReadOnlyList<ProductTagDto>> GetAllAsync(
            CancellationToken cancellationToken = default)
        {
            return await _dbContext.ProductTags
                .AsNoTracking()
                .OrderBy(x => x.Name)
                .Select(x => new ProductTagDto
                {
                    Id = x.Id,
                    Name = x.Name,
                    Slug = x.Slug
                })
                .ToListAsync(cancellationToken);
        }

        public async Task<ProductTagDto?> GetByIdAsync(
            Guid tagId,
            CancellationToken cancellationToken = default)
        {
            return await _dbContext.ProductTags
                .AsNoTracking()
                .Where(x => x.Id == tagId)
                .Select(x => new ProductTagDto
                {
                    Id = x.Id,
                    Name = x.Name,
                    Slug = x.Slug
                })
                .FirstOrDefaultAsync(cancellationToken);
        }

        public async Task<ProductTagDto> CreateAsync(
            CreateProductTagRequest request,
            CancellationToken cancellationToken = default)
        {
            var name = NormalizeRequired(request.Name, "Name");
            var slug = NormalizeSlug(request.Slug);

            var slugExists = await _dbContext.ProductTags
                .AnyAsync(x => x.Slug == slug, cancellationToken);

            if (slugExists)
                throw new InvalidOperationException(
                    "Product tag with this slug already exists.");

            var tag = new ProductTag
            {
                Id = Guid.NewGuid(),
                Name = name,
                Slug = slug
            };

            _dbContext.ProductTags.Add(tag);

            await _dbContext.SaveChangesAsync(cancellationToken);

            return ToDto(tag);
        }

        public async Task<ProductTagDto?> UpdateAsync(
            Guid tagId,
            UpdateProductTagRequest request,
            CancellationToken cancellationToken = default)
        {
            var tag = await _dbContext.ProductTags
                .FirstOrDefaultAsync(
                    x => x.Id == tagId,
                    cancellationToken);

            if (tag is null)
                return null;

            if (request.Name is not null)
                tag.Name = NormalizeRequired(request.Name, "Name");

            if (request.Slug is not null)
            {
                var slug = NormalizeSlug(request.Slug);

                var slugExists = await _dbContext.ProductTags
                    .AnyAsync(
                        x => x.Id != tagId &&
                             x.Slug == slug,
                        cancellationToken);

                if (slugExists)
                {
                    throw new InvalidOperationException(
                        "Product tag with this slug already exists.");
                }

                tag.Slug = slug;
            }

            await _dbContext.SaveChangesAsync(cancellationToken);

            return ToDto(tag);
        }

        public async Task<bool> DeleteAsync(
            Guid tagId,
            CancellationToken cancellationToken = default)
        {
            var tag = await _dbContext.ProductTags
                .FirstOrDefaultAsync(
                    x => x.Id == tagId,
                    cancellationToken);

            if (tag is null)
                return false;

            _dbContext.ProductTags.Remove(tag);

            await _dbContext.SaveChangesAsync(cancellationToken);

            return true;
        }

        public async Task<IReadOnlyList<ProductTagDto>> GetProductTagsAsync(
            Guid productId,
            CancellationToken cancellationToken = default)
        {
            return await (
                from relation in _dbContext.ProductTagRelations
                join tag in _dbContext.ProductTags
                    on relation.TagId equals tag.Id
                where relation.ProductId == productId
                orderby tag.Name
                select new ProductTagDto
                {
                    Id = tag.Id,
                    Name = tag.Name,
                    Slug = tag.Slug
                })
                .AsNoTracking()
                .ToListAsync(cancellationToken);
        }

        public async Task<bool> AddTagToProductAsync(
            Guid productId,
            Guid tagId,
            CancellationToken cancellationToken = default)
        {
            var productExists = await _dbContext.Products
                .AnyAsync(
                    x => x.Id == productId,
                    cancellationToken);

            if (!productExists)
                throw new InvalidOperationException("Product not found.");

            var tagExists = await _dbContext.ProductTags
                .AnyAsync(
                    x => x.Id == tagId,
                    cancellationToken);

            if (!tagExists)
                throw new InvalidOperationException("Product tag not found.");

            var relationExists = await _dbContext.ProductTagRelations
                .AnyAsync(
                    x => x.ProductId == productId &&
                         x.TagId == tagId,
                    cancellationToken);

            if (relationExists)
                return false;

            var relation = new ProductTagRelation
            {
                ProductId = productId,
                TagId = tagId
            };

            _dbContext.ProductTagRelations.Add(relation);

            await _dbContext.SaveChangesAsync(cancellationToken);

            return true;
        }

        public async Task<bool> RemoveTagFromProductAsync(
            Guid productId,
            Guid tagId,
            CancellationToken cancellationToken = default)
        {
            var relation = await _dbContext.ProductTagRelations
                .FirstOrDefaultAsync(
                    x => x.ProductId == productId &&
                         x.TagId == tagId,
                    cancellationToken);

            if (relation is null)
                return false;

            _dbContext.ProductTagRelations.Remove(relation);

            await _dbContext.SaveChangesAsync(cancellationToken);

            return true;
        }

        private static ProductTagDto ToDto(ProductTag tag)
        {
            return new ProductTagDto
            {
                Id = tag.Id,
                Name = tag.Name,
                Slug = tag.Slug
            };
        }

        private static string NormalizeRequired(
            string value,
            string fieldName)
        {
            var normalized = value.Trim();

            if (string.IsNullOrWhiteSpace(normalized))
            {
                throw new ArgumentException(
                    $"{fieldName} is required.");
            }

            return normalized;
        }

        private static string NormalizeSlug(string slug)
        {
            var normalized = slug.Trim().ToLowerInvariant();

            if (string.IsNullOrWhiteSpace(normalized))
                throw new ArgumentException("Slug is required.");

            return normalized;
        }
    }
}
