using Application.Abstractions;
using Application.Catalog;
using Application.Common;
using Application.Products;
using Contracts.Catalog;
using Domain.Entities;
using Domain.Entities.Attribute;
using Domain.Entities.Product;
using Domain.Enums;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using DomainAttribute = Domain.Entities.Attribute.Attribute;

namespace WebApi.Tests;

internal static class CatalogTestData
{
    internal sealed record Seed(Category Root, Category Child, Brand Brand, Seller Seller, Store Store);

    internal sealed record Actor(Guid? UserId) : ICurrentActor
    {
        public string Name => "Test Moderator";
        public string Role => "Moderator";
    }

    internal static async Task<Seed> SeedAsync(ApplicationDbContext db)
    {
        var root = new Category
        {
            Id = Guid.NewGuid(),
            Name = "Electronics",
            Slug = "electronics",
            IsActive = true,
        };
        var child = new Category
        {
            Id = Guid.NewGuid(),
            ParentId = root.Id,
            Name = "Phones",
            Slug = "phones",
            Level = 1,
            IsActive = true,
        };
        var brand = new Brand
        {
            Id = Guid.NewGuid(),
            Name = "Brand",
            Slug = "brand",
            IsActive = true,
        };
        var seller = new Seller
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            CompanyName = "Seller Alpha",
            TaxNumber = "QA",
            Status = SellerStatus.ACTIVE,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        var store = new Store
        {
            Id = Guid.NewGuid(),
            SellerId = seller.Id,
            Name = "Alpha Store",
            Slug = "alpha",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        db.AddRange(root, child, brand, seller, store);
        await db.SaveChangesAsync();
        return new(root, child, brand, seller, store);
    }

    internal static SaveCatalogProductRequest Request(Seed seed, string slug = "phone") =>
        new()
        {
            Name = "Phone",
            Slug = slug,
            CategoryId = seed.Child.Id,
            BrandId = seed.Brand.Id,
            StoreId = seed.Store.Id,
            Attributes = [],
            Variants =
            [
                new()
                {
                    Name = "Blue / 128 GB",
                    Sku = slug + "-BLUE",
                    Price = 100,
                    OldPrice = 120,
                    StockQuantity = 2,
                },
                new()
                {
                    Name = "Red / 256 GB",
                    Sku = slug + "-RED",
                    Price = 200,
                    StockQuantity = 3,
                },
            ],
        };
}

public sealed class CatalogImprovementsTests
{
    private static ApplicationDbContext Database() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    [Fact]
    public async Task Filters_CombineDescendantsBrandSellerPriceAndStock_AndMapTotals()
    {
        await using var db = Database();
        var seed = await CatalogTestData.SeedAsync(db);
        var service = new CatalogService(db);
        var first = await service.SaveAsync(null, seed.Store.Id, CatalogTestData.Request(seed), default);
        var other = CatalogTestData.Request(seed, "other");
        other.Variants!.ForEach(v => v.StockQuantity = 0);
        await service.SaveAsync(null, seed.Store.Id, other, default);
        var result = await service.ListAsync(
            1,
            12,
            "seller alpha",
            "DRAFT",
            false,
            seed.Root.Id,
            "priceasc",
            default,
            null,
            new()
            {
                BrandId = seed.Brand.Id,
                SellerId = seed.Seller.Id,
                MinPrice = 150,
                MaxPrice = 220,
                Stock = "low",
            }
        );
        var found = Assert.Single(result.Content);
        Assert.Equal(first!.Id, found.Id);
        Assert.Equal(100, found.Price);
        Assert.Equal(5, found.StockQuantity);
        Assert.Equal("Seller Alpha", found.SellerName);
        Assert.Equal(2, found.Variants.Count);
        Assert.Single(
            (
                await service.ListAsync(
                    1,
                    12,
                    null,
                    null,
                    false,
                    null,
                    "stockasc",
                    default,
                    null,
                    new() { Stock = "out" }
                )
            ).Content
        );
        Assert.Empty((await service.ListAsync(1, 12, null, null, false, null, null, default, Guid.NewGuid())).Content);
        Assert.Contains(
            (await service.FilterOptionsAsync(null, default)).Categories,
            c => c.Id == seed.Child.Id && c.Name == "Electronics → Phones"
        );
        Assert.Empty((await service.FilterOptionsAsync(Guid.NewGuid(), default)).Sellers);
    }

    [Theory]
    [InlineData(-1, 100, "available", "newest")]
    [InlineData(200, 100, "available", "newest")]
    [InlineData(0, 100, "unknown", "newest")]
    [InlineData(0, 100, "available", "unknown")]
    public async Task Filters_RejectInvalidRangesAndValues(decimal min, decimal max, string stock, string sort)
    {
        await using var db = Database();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            new CatalogService(db).ListAsync(
                1,
                12,
                null,
                null,
                false,
                null,
                sort,
                default,
                null,
                new()
                {
                    MinPrice = min,
                    MaxPrice = max,
                    Stock = stock,
                }
            )
        );
    }

    [Fact]
    public async Task Save_PreservesVariantIdsAndTypedInheritedCharacteristics_AndDeactivatesOmittedVariants()
    {
        await using var db = Database();
        var seed = await CatalogTestData.SeedAsync(db);
        var service = new CatalogService(db);
        var material = new DomainAttribute(
            Guid.NewGuid(),
            seed.Root.Id,
            "Material",
            "material",
            AttributeType.STRING,
            true,
            false,
            false,
            null,
            0
        );
        var color = new DomainAttribute(
            Guid.NewGuid(),
            seed.Child.Id,
            "Color",
            "color",
            AttributeType.SELECT,
            false,
            false,
            false,
            null,
            1
        );
        var option = new AttributeOption
        {
            Id = Guid.NewGuid(),
            AttributeId = color.Id,
            Value = "Blue",
        };
        db.AddRange(material, color, option);
        await db.SaveChangesAsync();
        var request = CatalogTestData.Request(seed);
        request.Attributes!.AddRange([
            new() { AttributeId = material.Id, StringValue = "Aluminum" },
            new()
            {
                AttributeId = color.Id,
                OptionId = option.Id,
                VariantId = request.Variants![0].Id,
            },
        ]);
        var created = (await service.SaveAsync(null, seed.Store.Id, request, default))!;
        Assert.Equal(2, created.Attributes.Count);
        Assert.Equal(2, created.Variants.Count);
        request.ExpectedUpdatedAt = created.UpdatedAt;
        var retained = request.Variants![0].Id;
        var omitted = request.Variants[1].Id;
        request.Variants.RemoveAt(1);
        request.Variants[0].Price = 110;
        var updated = (await service.SaveAsync(created.Id, Guid.Empty, request, default))!;
        Assert.Equal(110, updated.Variants.Single(v => v.Id == retained).Price);
        Assert.False(updated.Variants.Single(v => v.Id == omitted).IsActive);
        Assert.Equal(2, updated.Attributes.Count);
    }

    [Fact]
    public async Task Save_RejectsForeignVariantsAndCharacteristics_BeforeChangingProduct()
    {
        await using var db = Database();
        var seed = await CatalogTestData.SeedAsync(db);
        var service = new CatalogService(db);
        var request = CatalogTestData.Request(seed);
        var product = (await service.SaveAsync(null, seed.Store.Id, request, default))!;
        var foreign = (
            await service.SaveAsync(null, seed.Store.Id, CatalogTestData.Request(seed, "foreign"), default)
        )!;
        request.Name = "Must not persist";
        request.Variants![0].Id = foreign.Variants[0].Id;
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.SaveAsync(product.Id, Guid.Empty, request, default)
        );
        Assert.Equal("Phone", (await service.GetAsync(product.Id, default))!.Name);
        request.Variants[0].Id = product.Variants[0].Id;
        request.Attributes!.Add(new() { AttributeId = Guid.NewGuid(), StringValue = "Foreign" });
        await Assert.ThrowsAsync<ArgumentException>(() => service.SaveAsync(product.Id, Guid.Empty, request, default));
        Assert.Equal("Phone", (await service.GetAsync(product.Id, default))!.Name);
    }

    [Fact]
    public async Task Save_RejectsStaleEditorAndDuplicateSku_WithoutLosingExistingVariants()
    {
        await using var db = Database();
        var seed = await CatalogTestData.SeedAsync(db);
        var service = new CatalogService(db);
        var request = CatalogTestData.Request(seed);
        var product = (await service.SaveAsync(null, seed.Store.Id, request, default))!;
        request.ExpectedUpdatedAt = product.UpdatedAt.AddMinutes(-1);
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.SaveAsync(product.Id, Guid.Empty, request, default)
        );
        request.ExpectedUpdatedAt = product.UpdatedAt;
        request.Variants![1].Sku = request.Variants[0].Sku;
        await Assert.ThrowsAsync<ArgumentException>(() => service.SaveAsync(product.Id, Guid.Empty, request, default));
        Assert.Equal(2, await db.ProductVariants.CountAsync(v => v.ProductId == product.Id));
    }

    [Fact]
    public async Task Moderation_RequiresReason_AndKeepsHistoryAcrossEditingAndResubmission()
    {
        await using var db = Database();
        var seed = await CatalogTestData.SeedAsync(db);
        var catalog = new CatalogService(db);
        var request = CatalogTestData.Request(seed);
        var product = (await catalog.SaveAsync(null, seed.Store.Id, request, default))!;
        var service = new ProductService(db, new CatalogTestData.Actor(Guid.NewGuid()));
        await service.SubmitForModerationAsync(product.Id);
        await Assert.ThrowsAsync<ArgumentException>(() => service.RejectAsync(product.Id, "   "));
        Assert.Equal(ProductStatus.PENDING_MODERATION, (await db.Products.SingleAsync()).Status);
        await service.RejectAsync(product.Id, "  Add the warranty details.  ");
        var rejected = (await catalog.ModerationHistoryAsync(product.Id, default)).First();
        Assert.Equal("Rejected", rejected.Decision);
        Assert.Equal("Add the warranty details.", rejected.Reason);
        request.ExpectedUpdatedAt = null;
        await catalog.SaveAsync(product.Id, Guid.Empty, request, default);
        Assert.Equal("Rejected", (await catalog.ModerationHistoryAsync(product.Id, default)).First().Decision);
        await service.SubmitForModerationAsync(product.Id);
        await service.ApproveAsync(product.Id);
        var history = await catalog.ModerationHistoryAsync(product.Id, default);
        Assert.Equal(4, history.Count);
        Assert.Equal("Approved", history[0].Decision);
        Assert.Contains(history, h => h.Reason == "Add the warranty details.");
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.RejectAsync(product.Id, "A second decision must fail.")
        );
    }

    [Theory]
    [InlineData(AttributeType.STRING, true)]
    [InlineData(AttributeType.STRING, false)]
    [InlineData(AttributeType.NUMBER, true)]
    [InlineData(AttributeType.NUMBER, false)]
    [InlineData(AttributeType.BOOLEAN, true)]
    [InlineData(AttributeType.BOOLEAN, false)]
    [InlineData(AttributeType.DATE, true)]
    [InlineData(AttributeType.DATE, false)]
    [InlineData(AttributeType.SELECT, true)]
    [InlineData(AttributeType.SELECT, false)]
    [InlineData(AttributeType.MULTI_SELECT, true)]
    [InlineData(AttributeType.MULTI_SELECT, false)]
    public async Task CharacteristicValues_EnforceTheirTypeAndOptionOwnership(AttributeType type, bool valid)
    {
        await using var db = Database();
        var seed = await CatalogTestData.SeedAsync(db);
        var definition = new DomainAttribute(
            Guid.NewGuid(),
            seed.Child.Id,
            "Parameter",
            "parameter",
            type,
            true,
            false,
            false,
            null,
            0
        );
        var option = new AttributeOption
        {
            Id = Guid.NewGuid(),
            AttributeId = definition.Id,
            Value = "Option",
        };
        db.AddRange(definition, option);
        await db.SaveChangesAsync();
        var value = new CatalogAttributeInput { AttributeId = definition.Id };
        switch (type)
        {
            case AttributeType.STRING:
                value.StringValue = valid ? "Text" : " ";
                break;
            case AttributeType.DATE:
                value.StringValue = valid ? "2026-10-17" : "17/10/2026";
                break;
            case AttributeType.NUMBER:
                value.NumberValue = valid ? 12.3456m : 12.34567m;
                break;
            case AttributeType.BOOLEAN:
                value.BoolValue = false;
                if (!valid)
                    value.NumberValue = 0;
                break;
            default:
                value.OptionId = valid ? option.Id : Guid.NewGuid();
                break;
        }
        var request = CatalogTestData.Request(seed);
        request.Attributes!.Add(value);
        if (valid)
            Assert.Single((await new CatalogService(db).SaveAsync(null, seed.Store.Id, request, default))!.Attributes);
        else
        {
            await Assert.ThrowsAsync<ArgumentException>(() =>
                new CatalogService(db).SaveAsync(null, seed.Store.Id, request, default)
            );
            Assert.Empty(await db.Products.ToListAsync());
        }
    }
}
