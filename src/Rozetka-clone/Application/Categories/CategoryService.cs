using Application.Abstractions;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Categories
{
    public sealed class CategoryService : ICategoryService
    {
        private readonly IApplicationDbContext _dbContext;

        public CategoryService(IApplicationDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task<IReadOnlyList<CategoryTreeDto>> GetTreeAsync(
            CancellationToken cancellationToken = default)
        {
            var categories = await _dbContext.Categories
                .AsNoTracking()
                .OrderBy(x => x.Level)
                .ThenBy(x => x.SortOrder)
                .ThenBy(x => x.Name)
                .Select(x => new CategoryDto
                {
                    Id = x.Id,
                    ParentId = x.ParentId,
                    Name = x.Name,
                    Slug = x.Slug,
                    Description = x.Description,
                    ImageUrl = x.ImageUrl,
                    IsActive = x.IsActive,
                    SortOrder = x.SortOrder,
                    Level = x.Level
                })
                .ToListAsync(cancellationToken);

            return BuildTree(categories);
        }

        public async Task<CategoryDto?> GetByIdAsync(
            Guid id,
            CancellationToken cancellationToken = default)
        {
            return await _dbContext.Categories
                .AsNoTracking()
                .Where(x => x.Id == id)
                .Select(x => new CategoryDto
                {
                    Id = x.Id,
                    ParentId = x.ParentId,
                    Name = x.Name,
                    Slug = x.Slug,
                    Description = x.Description,
                    ImageUrl = x.ImageUrl,
                    IsActive = x.IsActive,
                    SortOrder = x.SortOrder,
                    Level = x.Level
                })
                .FirstOrDefaultAsync(cancellationToken);
        }

        public async Task<CategoryDto> CreateAsync(
            CreateCategoryRequest request,
            CancellationToken cancellationToken = default)
        {
            var name = request.Name.Trim();
            var slug = NormalizeSlug(request.Slug);

            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Category name is required.");

            if (string.IsNullOrWhiteSpace(slug))
                throw new ArgumentException("Category slug is required.");

            var slugExists = await _dbContext.Categories
                .AnyAsync(x => x.Slug == slug, cancellationToken);

            if (slugExists)
                throw new InvalidOperationException(
                    $"Category with slug '{slug}' already exists.");

            var level = 0;

            if (request.ParentId.HasValue)
            {
                var parent = await _dbContext.Categories
                    .AsNoTracking()
                    .FirstOrDefaultAsync(
                        x => x.Id == request.ParentId.Value,
                        cancellationToken);

                if (parent is null)
                    throw new InvalidOperationException("Parent category not found.");

                level = parent.Level + 1;
            }

            var category = new Category
            {
                Id = Guid.NewGuid(),
                ParentId = request.ParentId,
                Name = name,
                Slug = slug,
                Description = NormalizeOptional(request.Description),
                ImageUrl = NormalizeOptional(request.ImageUrl),
                IsActive = request.IsActive,
                SortOrder = request.SortOrder,
                Level = level
            };

            _dbContext.Categories.Add(category);

            await _dbContext.SaveChangesAsync(cancellationToken);

            return ToDto(category);
        }

        public async Task<CategoryDto?> UpdateAsync(
            Guid id,
            UpdateCategoryRequest request,
            CancellationToken cancellationToken = default)
        {
            var category = await _dbContext.Categories
                .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

            if (category is null)
                return null;

            if (request.Name is not null)
            {
                var name = request.Name.Trim();

                if (string.IsNullOrWhiteSpace(name))
                    throw new ArgumentException("Category name cannot be empty.");

                category.Name = name;
            }

            if (request.Slug is not null)
            {
                var slug = NormalizeSlug(request.Slug);

                if (string.IsNullOrWhiteSpace(slug))
                    throw new ArgumentException("Category slug cannot be empty.");

                var slugExists = await _dbContext.Categories
                    .AnyAsync(
                        x => x.Id != id && x.Slug == slug,
                        cancellationToken);

                if (slugExists)
                    throw new InvalidOperationException(
                        $"Category with slug '{slug}' already exists.");

                category.Slug = slug;
            }

            if (request.Description is not null)
                category.Description = NormalizeOptional(request.Description);

            if (request.ImageUrl is not null)
                category.ImageUrl = NormalizeOptional(request.ImageUrl);

            if (request.IsActive.HasValue)
                category.IsActive = request.IsActive.Value;

            if (request.SortOrder.HasValue)
                category.SortOrder = request.SortOrder.Value;

            await _dbContext.SaveChangesAsync(cancellationToken);

            return ToDto(category);
        }

        public async Task<bool> MoveAsync(
            Guid categoryId,
            Guid? newParentId,
            CancellationToken cancellationToken = default)
        {
            var categories = await _dbContext.Categories
                .ToListAsync(cancellationToken);

            var category = categories.FirstOrDefault(x => x.Id == categoryId);

            if (category is null)
                return false;

            if (newParentId == categoryId)
                throw new InvalidOperationException(
                    "Category cannot be its own parent.");

            Category? newParent = null;

            if (newParentId.HasValue)
            {
                newParent = categories.FirstOrDefault(
                    x => x.Id == newParentId.Value);

                if (newParent is null)
                    throw new InvalidOperationException(
                        "Parent category not found.");

                if (IsDescendant(categories, categoryId, newParent.Id))
                    throw new InvalidOperationException(
                        "Category cannot be moved into its own descendant.");
            }

            category.ParentId = newParentId;

            var newLevel = newParent is null
                ? 0
                : newParent.Level + 1;

            var levelDifference = newLevel - category.Level;

            category.Level = newLevel;

            UpdateDescendantLevels(
                categories,
                category.Id,
                levelDifference);

            await _dbContext.SaveChangesAsync(cancellationToken);

            return true;
        }

        private static IReadOnlyList<CategoryTreeDto> BuildTree(
            IReadOnlyList<CategoryDto> categories)
        {
            var nodes = categories.ToDictionary(
                x => x.Id,
                x => new CategoryTreeDto
                {
                    Id = x.Id,
                    ParentId = x.ParentId,
                    Name = x.Name,
                    Slug = x.Slug,
                    Description = x.Description,
                    ImageUrl = x.ImageUrl,
                    IsActive = x.IsActive,
                    SortOrder = x.SortOrder,
                    Level = x.Level
                });

            var roots = new List<CategoryTreeDto>();

            foreach (var category in categories)
            {
                var node = nodes[category.Id];

                if (category.ParentId.HasValue &&
                    nodes.TryGetValue(category.ParentId.Value, out var parent))
                {
                    parent.Children.Add(node);
                }
                else
                {
                    roots.Add(node);
                }
            }

            SortTree(roots);

            return roots;
        }

        private static void SortTree(List<CategoryTreeDto> categories)
        {
            categories.Sort((left, right) =>
            {
                var sortOrderComparison =
                    left.SortOrder.CompareTo(right.SortOrder);

                return sortOrderComparison != 0
                    ? sortOrderComparison
                    : string.Compare(
                        left.Name,
                        right.Name,
                        StringComparison.OrdinalIgnoreCase);
            });

            foreach (var category in categories)
                SortTree(category.Children);
        }

        private static bool IsDescendant(
            IReadOnlyCollection<Category> categories,
            Guid categoryId,
            Guid possibleDescendantId)
        {
            var current = categories.FirstOrDefault(
                x => x.Id == possibleDescendantId);

            while (current?.ParentId is Guid parentId)
            {
                if (parentId == categoryId)
                    return true;

                current = categories.FirstOrDefault(
                    x => x.Id == parentId);
            }

            return false;
        }

        private static void UpdateDescendantLevels(
            IReadOnlyCollection<Category> categories,
            Guid parentId,
            int levelDifference)
        {
            var children = categories
                .Where(x => x.ParentId == parentId)
                .ToList();

            foreach (var child in children)
            {
                child.Level += levelDifference;

                UpdateDescendantLevels(
                    categories,
                    child.Id,
                    levelDifference);
            }
        }

        private static CategoryDto ToDto(Category category)
        {
            return new CategoryDto
            {
                Id = category.Id,
                ParentId = category.ParentId,
                Name = category.Name,
                Slug = category.Slug,
                Description = category.Description,
                ImageUrl = category.ImageUrl,
                IsActive = category.IsActive,
                SortOrder = category.SortOrder,
                Level = category.Level
            };
        }

        private static string NormalizeSlug(string slug)
        {
            return slug.Trim().ToLowerInvariant();
        }

        private static string? NormalizeOptional(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            return value.Trim();
        }

    }
}