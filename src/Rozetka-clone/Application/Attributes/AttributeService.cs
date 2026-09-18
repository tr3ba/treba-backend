using Application.Abstractions;
using Domain.Entities.Attribute;
using Domain.Enums;
using Microsoft.EntityFrameworkCore;
using DomainAttribute = Domain.Entities.Attribute.Attribute;

namespace Application.Attributes
{
    public sealed class AttributeService : IAttributeService
    {
        private readonly IApplicationDbContext _dbContext;

        public AttributeService(IApplicationDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task<IReadOnlyList<AttributeDto>> GetByCategoryAsync(
            Guid categoryId,
            CancellationToken cancellationToken = default)
        {
            return await _dbContext.Attributes
                .AsNoTracking()
                .Where(x => x.CategoryId == categoryId)
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.Name)
                .Select(x => new AttributeDto
                {
                    Id = x.Id,
                    CategoryId = x.CategoryId,
                    Name = x.Name,
                    Code = x.Code,
                    Type = x.Type,
                    IsRequired = x.IsRequired,
                    IsFilterable = x.IsFilterable,
                    IsComparable = x.IsComparable,
                    Unit = x.Unit,
                    SortOrder = x.SortOrder
                })
                .ToListAsync(cancellationToken);
        }

        public async Task<AttributeDto?> GetByIdAsync(
            Guid id,
            CancellationToken cancellationToken = default)
        {
            return await _dbContext.Attributes
                .AsNoTracking()
                .Where(x => x.Id == id)
                .Select(x => new AttributeDto
                {
                    Id = x.Id,
                    CategoryId = x.CategoryId,
                    Name = x.Name,
                    Code = x.Code,
                    Type = x.Type,
                    IsRequired = x.IsRequired,
                    IsFilterable = x.IsFilterable,
                    IsComparable = x.IsComparable,
                    Unit = x.Unit,
                    SortOrder = x.SortOrder
                })
                .FirstOrDefaultAsync(cancellationToken);
        }

        public async Task<AttributeDto> CreateAsync(
            CreateAttributeRequest request,
            CancellationToken cancellationToken = default)
        {
            var name = request.Name.Trim();
            var code = NormalizeCode(request.Code);

            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Attribute name is required.");

            if (string.IsNullOrWhiteSpace(code))
                throw new ArgumentException("Attribute code is required.");

            var categoryExists = await _dbContext.Categories
                .AnyAsync(
                    x => x.Id == request.CategoryId,
                    cancellationToken);

            if (!categoryExists)
                throw new InvalidOperationException("Category not found.");

            var codeExists = await _dbContext.Attributes
                .AnyAsync(
                    x => x.Code == code,
                    cancellationToken);

            if (codeExists)
                throw new InvalidOperationException(
                    $"Attribute with code '{code}' already exists.");

            var attribute = new DomainAttribute(
                Guid.NewGuid(),
                request.CategoryId,
                name,
                code,
                request.Type,
                request.IsRequired,
                request.IsFilterable,
                request.IsComparable,
                NormalizeOptional(request.Unit),
                request.SortOrder);

            _dbContext.Attributes.Add(attribute);

            await _dbContext.SaveChangesAsync(cancellationToken);

            return ToDto(attribute);
        }

        public async Task<AttributeDto?> UpdateAsync(
            Guid id,
            UpdateAttributeRequest request,
            CancellationToken cancellationToken = default)
        {
            var attribute = await _dbContext.Attributes
                .FirstOrDefaultAsync(
                    x => x.Id == id,
                    cancellationToken);

            if (attribute is null)
                return null;

            var name = request.Name is null
                ? attribute.Name
                : request.Name.Trim();

            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException(
                    "Attribute name cannot be empty.");

            var code = request.Code is null
                ? attribute.Code
                : NormalizeCode(request.Code);

            if (string.IsNullOrWhiteSpace(code))
                throw new ArgumentException(
                    "Attribute code cannot be empty.");

            if (request.Code is not null)
            {
                var codeExists = await _dbContext.Attributes
                    .AnyAsync(
                        x => x.Id != id && x.Code == code,
                        cancellationToken);

                if (codeExists)
                    throw new InvalidOperationException(
                        $"Attribute with code '{code}' already exists.");
            }

            attribute.Update(
                name,
                code,
                request.Type ?? attribute.Type,
                request.IsRequired ?? attribute.IsRequired,
                request.IsFilterable ?? attribute.IsFilterable,
                request.IsComparable ?? attribute.IsComparable,
                request.Unit is null
                    ? attribute.Unit
                    : NormalizeOptional(request.Unit),
                request.SortOrder ?? attribute.SortOrder);

            await _dbContext.SaveChangesAsync(cancellationToken);

            return ToDto(attribute);
        }

        public async Task<IReadOnlyList<AttributeOptionDto>> GetOptionsAsync(
            Guid attributeId,
            CancellationToken cancellationToken = default)
        {
            return await _dbContext.AttributeOptions
                .AsNoTracking()
                .Where(x => x.AttributeId == attributeId)
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.Value)
                .Select(x => new AttributeOptionDto
                {
                    Id = x.Id,
                    AttributeId = x.AttributeId,
                    Value = x.Value,
                    DisplayValue = x.DisplayValue,
                    SortOrder = x.SortOrder
                })
                .ToListAsync(cancellationToken);
        }

        public async Task<AttributeOptionDto> CreateOptionAsync(
            Guid attributeId,
            CreateAttributeOptionRequest request,
            CancellationToken cancellationToken = default)
        {
            var attribute = await _dbContext.Attributes
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.Id == attributeId,
                    cancellationToken);

            if (attribute is null)
                throw new InvalidOperationException("Attribute not found.");

            if (attribute.Type is not AttributeType.SELECT
                and not AttributeType.MULTI_SELECT)
            {
                throw new InvalidOperationException(
                    "Options can only be added to SELECT or MULTI_SELECT attributes.");
            }

            var value = request.Value.Trim();

            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException(
                    "Attribute option value is required.");

            var valueExists = await _dbContext.AttributeOptions
                .AnyAsync(
                    x => x.AttributeId == attributeId &&
                         x.Value == value,
                    cancellationToken);

            if (valueExists)
                throw new InvalidOperationException(
                    $"Option '{value}' already exists for this attribute.");

            var option = new AttributeOption
            {
                Id = Guid.NewGuid(),
                AttributeId = attributeId,
                Value = value,
                DisplayValue = NormalizeOptional(request.DisplayValue),
                SortOrder = request.SortOrder
            };

            _dbContext.AttributeOptions.Add(option);

            await _dbContext.SaveChangesAsync(cancellationToken);

            return ToDto(option);
        }

        public async Task<AttributeOptionDto?> UpdateOptionAsync(
            Guid attributeId,
            Guid optionId,
            UpdateAttributeOptionRequest request,
            CancellationToken cancellationToken = default)
        {
            var option = await _dbContext.AttributeOptions
                .FirstOrDefaultAsync(
                    x => x.Id == optionId &&
                         x.AttributeId == attributeId,
                    cancellationToken);

            if (option is null)
                return null;

            if (request.Value is not null)
            {
                var value = request.Value.Trim();

                if (string.IsNullOrWhiteSpace(value))
                    throw new ArgumentException(
                        "Attribute option value cannot be empty.");

                var valueExists = await _dbContext.AttributeOptions
                    .AnyAsync(
                        x => x.AttributeId == attributeId &&
                             x.Id != optionId &&
                             x.Value == value,
                        cancellationToken);

                if (valueExists)
                    throw new InvalidOperationException(
                        $"Option '{value}' already exists for this attribute.");

                option.Value = value;
            }

            if (request.DisplayValue is not null)
                option.DisplayValue =
                    NormalizeOptional(request.DisplayValue);

            if (request.SortOrder.HasValue)
                option.SortOrder = request.SortOrder.Value;

            await _dbContext.SaveChangesAsync(cancellationToken);

            return ToDto(option);
        }

        private static AttributeDto ToDto(DomainAttribute attribute)
        {
            return new AttributeDto
            {
                Id = attribute.Id,
                CategoryId = attribute.CategoryId,
                Name = attribute.Name,
                Code = attribute.Code,
                Type = attribute.Type,
                IsRequired = attribute.IsRequired,
                IsFilterable = attribute.IsFilterable,
                IsComparable = attribute.IsComparable,
                Unit = attribute.Unit,
                SortOrder = attribute.SortOrder
            };
        }

        private static AttributeOptionDto ToDto(AttributeOption option)
        {
            return new AttributeOptionDto
            {
                Id = option.Id,
                AttributeId = option.AttributeId,
                Value = option.Value,
                DisplayValue = option.DisplayValue,
                SortOrder = option.SortOrder
            };
        }

        private static string NormalizeCode(string code)
        {
            return code.Trim().ToLowerInvariant();
        }

        private static string? NormalizeOptional(string? value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? null
                : value.Trim();
        }
    }
}
