using Application.Abstractions;
using Domain.Entities;
using Domain.Enums;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text;

namespace Application.ProductAttributeValues
{
    public sealed class ProductAttributeValueService
    : IProductAttributeValueService
    {
        private readonly IApplicationDbContext _dbContext;

        public ProductAttributeValueService(
            IApplicationDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task<IReadOnlyList<ProductAttributeValueDto>>
            GetByProductIdAsync(
                Guid productId,
                CancellationToken cancellationToken = default)
        {
            return await _dbContext.ProductAttributeValues
                .AsNoTracking()
                .Where(x => x.ProductId == productId)
                .Select(x => new ProductAttributeValueDto
                {
                    Id = x.Id,
                    ProductId = x.ProductId,
                    VariantId = x.VariantId,
                    AttributeId = x.AttributeId,
                    OptionId = x.OptionId,
                    StringValue = x.StringValue,
                    NumberValue = x.NumberValue,
                    BoolValue = x.BoolValue
                })
                .ToListAsync(cancellationToken);
        }

        public async Task<ProductAttributeValueDto?> GetByIdAsync(
            Guid productId,
            Guid valueId,
            CancellationToken cancellationToken = default)
        {
            return await _dbContext.ProductAttributeValues
                .AsNoTracking()
                .Where(x =>
                    x.Id == valueId &&
                    x.ProductId == productId)
                .Select(x => new ProductAttributeValueDto
                {
                    Id = x.Id,
                    ProductId = x.ProductId,
                    VariantId = x.VariantId,
                    AttributeId = x.AttributeId,
                    OptionId = x.OptionId,
                    StringValue = x.StringValue,
                    NumberValue = x.NumberValue,
                    BoolValue = x.BoolValue
                })
                .FirstOrDefaultAsync(cancellationToken);
        }

        public async Task<ProductAttributeValueDto> CreateAsync(
            Guid productId,
            CreateProductAttributeValueRequest request,
            CancellationToken cancellationToken = default)
        {
            var productExists = await _dbContext.Products
                .AnyAsync(
                    x => x.Id == productId,
                    cancellationToken);

            if (!productExists)
                throw new InvalidOperationException("Product not found.");

            await ValidateVariantAsync(
                productId,
                request.VariantId,
                cancellationToken);

            var attribute = await _dbContext.Attributes
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.Id == request.AttributeId,
                    cancellationToken);

            if (attribute is null)
                throw new InvalidOperationException("Attribute not found.");

            await ValidateValueAsync(
                attribute.Type,
                attribute.Id,
                request.OptionId,
                request.StringValue,
                request.NumberValue,
                request.BoolValue,
                cancellationToken);

            var value = new ProductAttributeValue
            {
                Id = Guid.NewGuid(),
                ProductId = productId,
                VariantId = request.VariantId,
                AttributeId = request.AttributeId,
                OptionId = request.OptionId,
                StringValue = NormalizeOptional(request.StringValue),
                NumberValue = request.NumberValue,
                BoolValue = request.BoolValue
            };

            _dbContext.ProductAttributeValues.Add(value);

            await _dbContext.SaveChangesAsync(cancellationToken);

            return ToDto(value);
        }

        public async Task<ProductAttributeValueDto?> UpdateAsync(
            Guid productId,
            Guid valueId,
            UpdateProductAttributeValueRequest request,
            CancellationToken cancellationToken = default)
        {
            var value = await _dbContext.ProductAttributeValues
                .FirstOrDefaultAsync(
                    x => x.Id == valueId &&
                         x.ProductId == productId,
                    cancellationToken);

            if (value is null)
                return null;

            var variantId = request.VariantId ?? value.VariantId;
            var attributeId = request.AttributeId ?? value.AttributeId;

            await ValidateVariantAsync(
                productId,
                variantId,
                cancellationToken);

            var attribute = await _dbContext.Attributes
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.Id == attributeId,
                    cancellationToken);

            if (attribute is null)
                throw new InvalidOperationException("Attribute not found.");

            var optionId = request.OptionId ?? value.OptionId;
            var stringValue = request.StringValue ?? value.StringValue;
            var numberValue = request.NumberValue ?? value.NumberValue;
            var boolValue = request.BoolValue ?? value.BoolValue;

            await ValidateValueAsync(
                attribute.Type,
                attribute.Id,
                optionId,
                stringValue,
                numberValue,
                boolValue,
                cancellationToken);

            value.VariantId = variantId;
            value.AttributeId = attributeId;
            value.OptionId = optionId;
            value.StringValue = NormalizeOptional(stringValue);
            value.NumberValue = numberValue;
            value.BoolValue = boolValue;

            await _dbContext.SaveChangesAsync(cancellationToken);

            return ToDto(value);
        }

        public async Task<bool> DeleteAsync(
            Guid productId,
            Guid valueId,
            CancellationToken cancellationToken = default)
        {
            var value = await _dbContext.ProductAttributeValues
                .FirstOrDefaultAsync(
                    x => x.Id == valueId &&
                         x.ProductId == productId,
                    cancellationToken);

            if (value is null)
                return false;

            _dbContext.ProductAttributeValues.Remove(value);

            await _dbContext.SaveChangesAsync(cancellationToken);

            return true;
        }

        private async Task ValidateVariantAsync(
            Guid productId,
            Guid? variantId,
            CancellationToken cancellationToken)
        {
            if (!variantId.HasValue)
                return;

            var exists = await _dbContext.ProductVariants
                .AnyAsync(
                    x => x.Id == variantId.Value &&
                         x.ProductId == productId,
                    cancellationToken);

            if (!exists)
            {
                throw new InvalidOperationException(
                    "Product variant not found or does not belong to this product.");
            }
        }

        private async Task ValidateValueAsync(
            AttributeType type,
            Guid attributeId,
            Guid? optionId,
            string? stringValue,
            decimal? numberValue,
            bool? boolValue,
            CancellationToken cancellationToken)
        {
            switch (type)
            {
                case AttributeType.STRING:
                case AttributeType.DATE:
                    if (string.IsNullOrWhiteSpace(stringValue))
                    {
                        throw new ArgumentException(
                            "String value is required for this attribute.");
                    }

                    if (optionId.HasValue ||
                        numberValue.HasValue ||
                        boolValue.HasValue)
                    {
                        throw new ArgumentException(
                            "Only StringValue is allowed for this attribute.");
                    }

                    break;

                case AttributeType.NUMBER:
                    if (!numberValue.HasValue)
                    {
                        throw new ArgumentException(
                            "NumberValue is required for NUMBER attribute.");
                    }

                    if (optionId.HasValue ||
                        !string.IsNullOrWhiteSpace(stringValue) ||
                        boolValue.HasValue)
                    {
                        throw new ArgumentException(
                            "Only NumberValue is allowed for NUMBER attribute.");
                    }

                    break;

                case AttributeType.BOOLEAN:
                    if (!boolValue.HasValue)
                    {
                        throw new ArgumentException(
                            "BoolValue is required for BOOLEAN attribute.");
                    }

                    if (optionId.HasValue ||
                        !string.IsNullOrWhiteSpace(stringValue) ||
                        numberValue.HasValue)
                    {
                        throw new ArgumentException(
                            "Only BoolValue is allowed for BOOLEAN attribute.");
                    }

                    break;

                case AttributeType.SELECT:
                case AttributeType.MULTI_SELECT:
                    if (!optionId.HasValue)
                    {
                        throw new ArgumentException(
                            "OptionId is required for this attribute.");
                    }

                    var optionExists = await _dbContext.AttributeOptions
                        .AnyAsync(
                            x => x.Id == optionId.Value &&
                                 x.AttributeId == attributeId,
                            cancellationToken);

                    if (!optionExists)
                    {
                        throw new InvalidOperationException(
                            "Attribute option not found or does not belong to this attribute.");
                    }

                    if (!string.IsNullOrWhiteSpace(stringValue) ||
                        numberValue.HasValue ||
                        boolValue.HasValue)
                    {
                        throw new ArgumentException(
                            "Only OptionId is allowed for SELECT attributes.");
                    }

                    break;

                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(type),
                        type,
                        "Unsupported attribute type.");
            }
        }

        private static ProductAttributeValueDto ToDto(
            ProductAttributeValue value)
        {
            return new ProductAttributeValueDto
            {
                Id = value.Id,
                ProductId = value.ProductId,
                VariantId = value.VariantId,
                AttributeId = value.AttributeId,
                OptionId = value.OptionId,
                StringValue = value.StringValue,
                NumberValue = value.NumberValue,
                BoolValue = value.BoolValue
            };
        }

        private static string? NormalizeOptional(string? value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? null
                : value.Trim();
        }
    }
}
