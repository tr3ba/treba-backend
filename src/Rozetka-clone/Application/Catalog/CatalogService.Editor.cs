using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Application.Common;
using Contracts.Catalog;
using Domain.Entities;
using Domain.Entities.Product;
using Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Application.Catalog;

public sealed partial class CatalogService
{
    private async Task<CatalogProductDto?> SaveProductAsync(Guid? id, Guid storeId, SaveCatalogProductRequest request, CancellationToken ct)
    {
        var errors = new List<ValidationResult>();
        if (!Validator.TryValidateObject(request, new(request), errors, true)) throw new ArgumentException(string.Join(" ", errors.Select(e => e.ErrorMessage)));
        if (string.IsNullOrWhiteSpace(request.Name)) throw new ArgumentException("Product name is required.");
        var product = id.HasValue ? await db.Products.SingleOrDefaultAsync(p => p.Id == id, ct)
            : new Product { Id = Guid.NewGuid(), StoreId = storeId, CreatedAt = DateTime.UtcNow };
        if (product is null) return null;
        if (product.Status is ProductStatus.PENDING_MODERATION or ProductStatus.ARCHIVED) throw new BusinessRuleException("A product under review or archived cannot be edited.");
        if (id.HasValue && request.ExpectedUpdatedAt.HasValue && request.ExpectedUpdatedAt != product.UpdatedAt)
            throw new BusinessRuleException("This product was changed by another employee. Reload it before saving.");
        if (!await db.Categories.AnyAsync(c => c.Id == request.CategoryId && c.IsActive, ct)) throw new ArgumentException("Select an active category.");
        if (!await db.Brands.AnyAsync(b => b.Id == request.BrandId && b.IsActive, ct)) throw new ArgumentException("Select an active brand.");
        var slug = request.Slug.Trim().ToLowerInvariant();
        if (await db.Products.AnyAsync(p => p.Slug == slug && p.Id != product.Id, ct)) throw new BusinessRuleException("Product slug is already in use.");
        var existing = await db.ProductVariants.Where(v => v.ProductId == product.Id).OrderBy(v => v.Sku).ToListAsync(ct);
        var variants = request.Variants ?? existing.Select(v => new CatalogVariantInput { Id = v.Id, Name = v.Name, Sku = v.Sku, Price = v.Price,
            OldPrice = v.OldPrice, StockQuantity = v.StockQuantity, IsActive = v.IsActive }).ToList();
        if (request.Variants is null)
        {
            if (variants.Count == 0) variants.Add(new() { Name = "Основний" });
            variants[0].Sku = request.Sku; variants[0].Price = request.Price; variants[0].StockQuantity = request.StockQuantity;
        }
        foreach (var variant in variants) { variant.Sku = variant.Sku.Trim().ToUpperInvariant(); variant.Name = variant.Name.Trim(); }
        if (variants.Select(v => v.Id).Distinct().Count() != variants.Count || variants.Select(v => v.Sku).Distinct(StringComparer.OrdinalIgnoreCase).Count() != variants.Count)
            throw new ArgumentException("Each variant must have a unique id and SKU.");
        var ids = variants.Select(v => v.Id).ToArray(); var skus = variants.Select(v => v.Sku).ToArray();
        if (await db.ProductVariants.AnyAsync(v => v.ProductId != product.Id && (ids.Contains(v.Id) || skus.Contains(v.Sku.ToUpper())), ct))
            throw new BusinessRuleException("A variant id or SKU belongs to another product.");
        var effectiveAttributes = request.Attributes ?? await db.ProductAttributeValues.AsNoTracking().Where(v => v.ProductId == product.Id)
            .Select(v => new CatalogAttributeInput { AttributeId = v.AttributeId, VariantId = v.VariantId, OptionId = v.OptionId,
                StringValue = v.StringValue, NumberValue = v.NumberValue, BoolValue = v.BoolValue }).ToListAsync(ct);
        await ValidateAttributesAsync(request.CategoryId, effectiveAttributes, variants, ct);

        // Validation is complete before changing tracked entities. All rows commit together.
        product.Name = request.Name.Trim(); product.Slug = slug; product.CategoryId = request.CategoryId; product.BrandId = request.BrandId;
        product.ShortDescription = request.ShortDescription.Trim(); product.Description = request.Description.Trim();
        product.WarrantyMonths = request.WarrantyMonths; product.CountryOfOrigin = request.CountryOfOrigin.Trim();
        product.Status = ProductStatus.DRAFT; product.UpdatedAt = DateTime.UtcNow;
        if (!id.HasValue) db.Products.Add(product);
        foreach (var input in variants)
        {
            var variant = existing.SingleOrDefault(v => v.Id == input.Id);
            if (variant is null)
            {
                variant = new(input.Id, product.Id, input.Sku, CatalogDuplicateService.Normalize(input.Barcode), input.Name, input.Price, input.OldPrice, null, null, null, null, null, input.IsActive);
                db.ProductVariants.Add(variant);
            }
            else variant.Update(input.Sku, input.Barcode is null ? variant.Barcode : CatalogDuplicateService.Normalize(input.Barcode), input.Name, input.Price, input.OldPrice, variant.CostPrice, variant.Weight, variant.Length, variant.Width, variant.Height, input.IsActive);
            variant.SetStock(input.StockQuantity);
        }
        var identity = await db.ProductIdentities.SingleOrDefaultAsync(i => i.ProductId == product.Id, ct);
        if (identity is null) { identity = new() { ProductId = product.Id }; db.ProductIdentities.Add(identity); }
        identity.ManufacturerPartNumber = (request.ManufacturerPartNumber ?? "").Trim(); identity.NormalizedPartNumber = CatalogDuplicateService.Normalize(request.ManufacturerPartNumber);
        // Keep variant ids referenced by carts and past purchases; omitted variants become inactive.
        if (request.Variants is not null)
            foreach (var variant in existing.Where(v => !ids.Contains(v.Id)))
                variant.Update(variant.Sku, variant.Barcode, variant.Name, variant.Price, variant.OldPrice, variant.CostPrice, variant.Weight, variant.Length, variant.Width, variant.Height, false);
        if (request.Attributes is not null)
        {
            db.ProductAttributeValues.RemoveRange(await db.ProductAttributeValues.Where(v => v.ProductId == product.Id).ToListAsync(ct));
            db.ProductAttributeValues.AddRange(request.Attributes.Select(a => new ProductAttributeValue { Id = Guid.NewGuid(), ProductId = product.Id,
                VariantId = a.VariantId, AttributeId = a.AttributeId, OptionId = a.OptionId, StringValue = a.StringValue?.Trim(), NumberValue = a.NumberValue, BoolValue = a.BoolValue }));
        }
        await db.SaveChangesAsync(ct);
        return await GetAsync(product.Id, ct);
    }

    private async Task ValidateAttributesAsync(Guid categoryId, List<CatalogAttributeInput> values, List<CatalogVariantInput> variants, CancellationToken ct)
    {
        var definitions = await AttributeDefinitionsAsync(categoryId, ct);
        var variantIds = variants.Select(v => v.Id).ToHashSet();
        foreach (var value in values)
        {
            var definition = definitions.SingleOrDefault(a => a.Id == value.AttributeId) ?? throw new ArgumentException("A characteristic does not belong to this category or its parents.");
            if (value.VariantId.HasValue && !variantIds.Contains(value.VariantId.Value)) throw new ArgumentException("The characteristic refers to another product variant.");
            var valid = definition.Type switch {
                "STRING" => !string.IsNullOrWhiteSpace(value.StringValue),
                "DATE" => DateOnly.TryParseExact(value.StringValue, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
                "NUMBER" => value.NumberValue is >= -99999999999999.9999m and <= 99999999999999.9999m && decimal.Round(value.NumberValue.Value, 4) == value.NumberValue,
                "BOOLEAN" => value.BoolValue.HasValue,
                "SELECT" or "MULTI_SELECT" => definition.Options.Any(o => o.Id == value.OptionId), _ => false };
            var populated = (string.IsNullOrWhiteSpace(value.StringValue) ? 0 : 1) + (value.NumberValue.HasValue ? 1 : 0) + (value.BoolValue.HasValue ? 1 : 0) + (value.OptionId.HasValue ? 1 : 0);
            if (!valid || populated != 1 || value.StringValue?.Length > 2000) throw new ArgumentException($"Enter a valid value for {definition.Name}.");
        }
        foreach (var group in values.GroupBy(v => (v.AttributeId, v.VariantId)))
        {
            var definition = definitions.Single(a => a.Id == group.Key.AttributeId);
            if (definition.Type != "MULTI_SELECT" && group.Count() > 1 || group.Where(v => v.OptionId.HasValue).Select(v => v.OptionId).Distinct().Count() != group.Count(v => v.OptionId.HasValue))
                throw new ArgumentException("A characteristic value is duplicated.");
        }
        foreach (var required in definitions.Where(d => d.IsRequired))
            if (!values.Any(v => v.AttributeId == required.Id && v.VariantId is null)
                && variants.Where(v => v.IsActive).Any(variant => !values.Any(v => v.AttributeId == required.Id && v.VariantId == variant.Id)))
                throw new ArgumentException($"Required characteristic: {required.Name}.");
    }
}
