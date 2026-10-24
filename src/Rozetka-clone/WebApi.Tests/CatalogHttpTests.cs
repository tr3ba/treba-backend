using System.Net;
using System.Net.Http.Json;
using Application.Catalog;
using Contracts.Catalog;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace WebApi.Tests;

public sealed class CatalogHttpTests
{
    [Fact]
    public async Task AttributeCreation_ValidatesInput_AndIsAdministratorOnly()
    {
        await using var server = await AdminHttpTests.ServerAsync();
        using var client = server.GetTestClient();
        Guid category;
        using (var scope = server.Services.CreateScope())
            category = (
                await CatalogTestData.SeedAsync(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>())
            )
                .Child
                .Id;
        client.DefaultRequestHeaders.Add("X-Test-Role", "Administrator");
        using var blank = await client.PostAsJsonAsync(
            "/api/v1/admin/catalog/attributes",
            new CreateCatalogAttributeRequest(category, "   ", null)
        );
        Assert.Equal(HttpStatusCode.BadRequest, blank.StatusCode);
        using var created = await client.PostAsJsonAsync(
            "/api/v1/admin/catalog/attributes",
            new CreateCatalogAttributeRequest(category, "Материал", null)
        );
        Assert.True(created.IsSuccessStatusCode, await created.Content.ReadAsStringAsync());
        var definitions = await client.GetFromJsonAsync<List<CatalogAttributeDefinitionDto>>(
            $"/api/v1/admin/catalog/attributes?categoryId={category}"
        );
        Assert.Equal("STRING", Assert.Single(definitions!).Type);
        client.DefaultRequestHeaders.Remove("X-Test-Role");
        client.DefaultRequestHeaders.Add("X-Test-Role", "Seller");
        using var forbidden = await client.PostAsJsonAsync(
            "/api/v1/admin/catalog/attributes",
            new CreateCatalogAttributeRequest(category, "Forbidden", null)
        );
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    [Fact]
    public async Task Seller_CanFilterOwnCatalog_AndCannotReadOrCopyOtherSellersProductsOrHistory()
    {
        await using var server = await AdminHttpTests.ServerAsync();
        using var client = server.GetTestClient();
        Guid user,
            own,
            foreign,
            root,
            brand;
        using (var scope = server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var seed = await CatalogTestData.SeedAsync(db);
            user = seed.Seller.UserId;
            root = seed.Root.Id;
            brand = seed.Brand.Id;
            var catalog = new CatalogService(db);
            own = (await catalog.SaveAsync(null, seed.Store.Id, CatalogTestData.Request(seed), default))!.Id;
            var other = new Domain.Entities.Store
            {
                Id = Guid.NewGuid(),
                SellerId = Guid.NewGuid(),
                Name = "Foreign",
                Slug = "foreign",
                IsActive = true,
            };
            db.Stores.Add(other);
            await db.SaveChangesAsync();
            foreign = (await catalog.SaveAsync(null, other.Id, CatalogTestData.Request(seed, "foreign"), default))!.Id;
        }
        client.DefaultRequestHeaders.Add("X-Test-Role", "Seller");
        client.DefaultRequestHeaders.Add("X-Test-UserId", user.ToString());
        var list = await client.GetFromJsonAsync<Contracts.Common.PagedResponse<CatalogProductDto>>(
            $"/api/v1/admin/catalog/products?categoryId={root}&brandId={brand}&minPrice=100&maxPrice=200&stock=low&sort=priceasc"
        );
        Assert.Equal(own, Assert.Single(list!.Content).Id);
        using var get = await client.GetAsync($"/api/v1/admin/catalog/products/{foreign}");
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
        using var history = await client.GetAsync($"/api/v1/admin/catalog/products/{foreign}/moderation-history");
        Assert.Equal(HttpStatusCode.NotFound, history.StatusCode);
        using var ownHistory = await client.GetAsync($"/api/v1/admin/catalog/products/{own}/moderation-history");
        Assert.Equal(HttpStatusCode.OK, ownHistory.StatusCode);
        var options = await client.GetFromJsonAsync<CatalogFilterOptionsDto>("/api/v1/admin/catalog/filter-options");
        Assert.Single(options!.Sellers);
    }

    [Fact]
    public async Task ModerationHttp_RejectsEmptyReason_AndExposesPersistedDecisionToProductOwner()
    {
        await using var server = await AdminHttpTests.ServerAsync();
        using var client = server.GetTestClient();
        Guid id,
            owner;
        using (var scope = server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var seed = await CatalogTestData.SeedAsync(db);
            owner = seed.Seller.UserId;
            id = (
                await new CatalogService(db).SaveAsync(null, seed.Store.Id, CatalogTestData.Request(seed), default)
            )!.Id;
        }
        client.DefaultRequestHeaders.Add("X-Test-Role", "Administrator");
        client.DefaultRequestHeaders.Add("X-Test-UserId", Guid.NewGuid().ToString());
        using var submitted = await client.PostAsync($"/api/v1/seller/products/{id}/submit", null);
        Assert.Equal(HttpStatusCode.NoContent, submitted.StatusCode);
        using var missing = await client.PostAsJsonAsync($"/api/v1/admin/products/{id}/reject", new { });
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        using var blank = await client.PostAsJsonAsync(
            $"/api/v1/admin/products/{id}/reject",
            new RejectProductRequest("   ")
        );
        Assert.Equal(HttpStatusCode.BadRequest, blank.StatusCode);
        using var rejected = await client.PostAsJsonAsync(
            $"/api/v1/admin/products/{id}/reject",
            new RejectProductRequest("Укажите гарантию.")
        );
        Assert.True(rejected.StatusCode == HttpStatusCode.NoContent, await rejected.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Remove("X-Test-Role");
        client.DefaultRequestHeaders.Add("X-Test-Role", "Seller");
        client.DefaultRequestHeaders.Remove("X-Test-UserId");
        client.DefaultRequestHeaders.Add("X-Test-UserId", owner.ToString());
        var history = await client.GetFromJsonAsync<List<ProductModerationDto>>(
            $"/api/v1/admin/catalog/products/{id}/moderation-history"
        );
        Assert.Equal("Укажите гарантию.", history![0].Reason);
        Assert.Equal(2, history.Count);
        using var forbidden = await client.PostAsJsonAsync(
            $"/api/v1/admin/products/{id}/reject",
            new RejectProductRequest("Seller must not moderate.")
        );
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }
}
