using Application.Carts;
using Domain.Entities.Product;
using Microsoft.EntityFrameworkCore;

namespace WebApi.Tests;

public sealed class CartMediaTests
{
    [Fact]
    public async Task CartReads_ReturnTheMainProductImage_AndKeepProductsWithoutImages()
    {
        await using var db = TelegramTestData.Memory(); var seed = await OperationsTests.SeedAsync(db);
        var service = new CartService(db);
        Assert.Null(Assert.Single((await service.AddItemAsync(seed.Customer.Id,
            new AddCartItemRequest { ProductVariantId = seed.Variant.Id, Quantity = 1 })).Items).ImageUrl);
        db.ProductImages.AddRange(
            new ProductImage { Id = Guid.NewGuid(), ProductId = seed.Product.Id, ImageUrl = "/uploads/other.jpg", SortOrder = 0 },
            new ProductImage { Id = Guid.NewGuid(), ProductId = seed.Product.Id, ImageUrl = "/uploads/main.jpg", IsMain = true, SortOrder = 10 });
        await db.SaveChangesAsync();
        Assert.Equal("/uploads/main.jpg", Assert.Single((await service.GetAsync(seed.Customer.Id)).Items).ImageUrl);
    }
}
