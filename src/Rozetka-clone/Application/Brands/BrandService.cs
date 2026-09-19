using Application.Abstractions;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Brands
{
    public sealed class BrandService : IBrandService
    {
        private readonly IApplicationDbContext _dbContext;

        public BrandService(IApplicationDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task<IReadOnlyList<BrandDto>> GetAllAsync(
            CancellationToken cancellationToken = default)
        {
            return await _dbContext.Brands
                .AsNoTracking()
                .OrderBy(x => x.Name)
                .Select(x => new BrandDto
                {
                    Id = x.Id,
                    Name = x.Name,
                    Slug = x.Slug,
                    Description = x.Description,
                    LogoUrl = x.LogoUrl,
                    IsActive = x.IsActive
                })
                .ToListAsync(cancellationToken);
        }

        public async Task<BrandDto?> GetByIdAsync(
            Guid id,
            CancellationToken cancellationToken = default)
        {
            return await _dbContext.Brands
                .AsNoTracking()
                .Where(x => x.Id == id)
                .Select(x => new BrandDto
                {
                    Id = x.Id,
                    Name = x.Name,
                    Slug = x.Slug,
                    Description = x.Description,
                    LogoUrl = x.LogoUrl,
                    IsActive = x.IsActive
                })
                .FirstOrDefaultAsync(cancellationToken);
        }

        public async Task<BrandDto> CreateAsync(
            CreateBrandRequest request,
            CancellationToken cancellationToken = default)
        {
            var name = request.Name.Trim();
            var slug = NormalizeSlug(request.Slug);

            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Brand name is required.");

            if (string.IsNullOrWhiteSpace(slug))
                throw new ArgumentException("Brand slug is required.");

            var slugExists = await _dbContext.Brands
                .AnyAsync(x => x.Slug == slug, cancellationToken);

            if (slugExists)
                throw new InvalidOperationException(
                    $"Brand with slug '{slug}' already exists.");

            var brand = new Brand
            {
                Id = Guid.NewGuid(),
                Name = name,
                Slug = slug,
                Description = NormalizeOptional(request.Description),
                LogoUrl = NormalizeOptional(request.LogoUrl),
                IsActive = request.IsActive
            };

            _dbContext.Brands.Add(brand);

            await _dbContext.SaveChangesAsync(cancellationToken);

            return ToDto(brand);
        }

        public async Task<BrandDto?> UpdateAsync(
            Guid id,
            UpdateBrandRequest request,
            CancellationToken cancellationToken = default)
        {
            var brand = await _dbContext.Brands
                .FirstOrDefaultAsync(
                    x => x.Id == id,
                    cancellationToken);

            if (brand is null)
                return null;

            if (request.Name is not null)
            {
                var name = request.Name.Trim();

                if (string.IsNullOrWhiteSpace(name))
                    throw new ArgumentException(
                        "Brand name cannot be empty.");

                brand.Name = name;
            }

            if (request.Slug is not null)
            {
                var slug = NormalizeSlug(request.Slug);

                if (string.IsNullOrWhiteSpace(slug))
                    throw new ArgumentException(
                        "Brand slug cannot be empty.");

                var slugExists = await _dbContext.Brands
                    .AnyAsync(
                        x => x.Id != id && x.Slug == slug,
                        cancellationToken);

                if (slugExists)
                    throw new InvalidOperationException(
                        $"Brand with slug '{slug}' already exists.");

                brand.Slug = slug;
            }

            if (request.Description is not null)
                brand.Description =
                    NormalizeOptional(request.Description);

            if (request.LogoUrl is not null)
                brand.LogoUrl =
                    NormalizeOptional(request.LogoUrl);

            if (request.IsActive.HasValue)
                brand.IsActive = request.IsActive.Value;

            await _dbContext.SaveChangesAsync(cancellationToken);

            return ToDto(brand);
        }

        private static BrandDto ToDto(Brand brand)
        {
            return new BrandDto
            {
                Id = brand.Id,
                Name = brand.Name,
                Slug = brand.Slug,
                Description = brand.Description,
                LogoUrl = brand.LogoUrl,
                IsActive = brand.IsActive
            };
        }

        private static string NormalizeSlug(string slug)
        {
            return slug.Trim().ToLowerInvariant();
        }

        private static string? NormalizeOptional(string? value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? null
                : value.Trim();
        }

    }
}