using System.Text.Json;
using Application.Catalog;
using Application.Common;
using Application.Products;
using Contracts.Catalog;
using Contracts.Telegram;
using Domain.Entities.Telegram;
using Domain.Entities.Users;
using Domain.Enums;
using Infrastructure.Administration;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using WebApi.Telegram;

namespace WebApi.Tests;

public sealed class SellerWorkspaceTests
{
    [Fact]
    public async Task SellerWizard_CreatesLinkedAccountProfileStoreAndGrants_AndNeverStoresPlaintextPassword()
    {
        await using var db=TelegramTestData.Memory();var seed=await TelegramTestData.SeedAsync(db);
        var request=new CreateSellerWorkspaceRequest{Email="NEWSELLER@example.test",Password="Workspace-test-password-123!",FirstName="Seller",LastName="Example",CompanyName="Example seller",StoreName="Example store",StoreSlug="example-store",TestSeller=true,AllCategories=true,Active=true};
        var service=new SellerProvisioningService(db);var result=await service.CreateAsync(request,default);
        var user=await db.Users.Include(u=>u.Role).SingleAsync(u=>u.Id==result.UserId);
        Assert.Equal("newseller@example.test",user.Email);Assert.Equal(Roles.Seller,user.Role!.Name);Assert.Equal(UserStatus.Active,user.Status);
        var seller=await db.Sellers.SingleAsync(s=>s.Id==result.SellerId);Assert.Equal(user.Id,seller.UserId);Assert.StartsWith("TEST-",seller.TaxNumber);
        Assert.Equal(seller.Id,(await db.Stores.SingleAsync(s=>s.Id==result.StoreId)).SellerId);
        Assert.Equal(2,await db.SellerCategoryGrants.CountAsync(g=>g.SellerId==seller.Id));
        Assert.DoesNotContain(request.Password,JsonSerializer.Serialize(result));
        await Assert.ThrowsAsync<UserDomainException>(()=>service.CreateAsync(request,default));
        Assert.Equal(2,await db.Users.CountAsync());
    }

    [Fact]
    public async Task SellerWizard_RejectsInvalidCategoriesBeforeCreatingAnything()
    {
        await using var db=TelegramTestData.Memory();await TelegramTestData.SeedAsync(db);
        var request=new CreateSellerWorkspaceRequest{Email="invalid@example.test",Password="Workspace-test-password-123!",FirstName="Seller",LastName="Example",CompanyName="Example",StoreName="Example",StoreSlug="invalid",TestSeller=true,CategoryIds=[Guid.NewGuid()]};
        await Assert.ThrowsAsync<ArgumentException>(()=>new SellerProvisioningService(db).CreateAsync(request,default));
        Assert.Single(await db.Users.ToListAsync());Assert.Single(await db.Sellers.ToListAsync());Assert.Single(await db.Stores.ToListAsync());
    }

    [Fact]
    public async Task Queue_DeduplicatesPendingLinks_ProcessesWithoutPublishing_AndIsScopedToOwner()
    {
        await using var db=TelegramTestData.Memory();var seed=await TelegramTestData.SeedAsync(db);await TelegramTestData.AllowImportAsync(db,seed);
        var clock=new TelegramTestData.Clock();var queue=new SellerImportQueue(db,new(db),clock);var input=new QueueImportsRequest([new(TelegramTestData.SourceUrl)]);
        var created=Assert.Single(await queue.AddAsync(seed.User.Id,input,default));
        Assert.Equal(created.Id,Assert.Single(await queue.AddAsync(seed.User.Id,input,default)).Id);
        Assert.Empty(await queue.ListAsync(Guid.NewGuid(),default));
        var runner=Runner(db,clock,new TelegramTestData.Source(),new Bot());Assert.True(await runner.ImportOneAsync(default));
        var finished=Assert.Single(await queue.ListAsync(seed.User.Id,default));Assert.Equal("Ready",finished.Status);Assert.NotNull(finished.PreviewId);
        Assert.Empty(await db.Products.ToListAsync());Assert.False(await runner.ImportOneAsync(default));
    }

    [Fact]
    public async Task Queue_FailureCanBeRetried_AndChangedSellerAccessPreventsProcessing()
    {
        await using var db=TelegramTestData.Memory();var seed=await TelegramTestData.SeedAsync(db);await TelegramTestData.AllowImportAsync(db,seed);var clock=new TelegramTestData.Clock();
        var queue=new SellerImportQueue(db,new(db),clock);var item=Assert.Single(await queue.AddAsync(seed.User.Id,new([new(TelegramTestData.SourceUrl)]),default));
        await Runner(db,clock,new FailingSource(),new Bot()).ImportOneAsync(default);
        Assert.Equal("Failed",Assert.Single(await queue.ListAsync(seed.User.Id,default)).Status);
        await queue.RetryAsync(seed.User.Id,item.Id,default);(await db.Users.SingleAsync(u=>u.Id==seed.User.Id)).Block();await db.SaveChangesAsync();
        await Runner(db,clock,new TelegramTestData.Source(),new Bot()).ImportOneAsync(default);
        Assert.Empty(await db.ProductImportDrafts.ToListAsync());Assert.Equal("Failed",Assert.Single(await queue.ListAsync(seed.User.Id,default)).Status);
        await Assert.ThrowsAsync<BusinessRuleException>(()=>queue.AddAsync(seed.User.Id,new([new(TelegramTestData.SourceUrl)]),default));
    }

    [Fact]
    public async Task Queue_RetryCanClassifyEachFailedLink_WithoutReaddingOrPublishing_AndRechecksAccess()
    {
        await using var db = TelegramTestData.Memory(); var seed = await TelegramTestData.SeedAsync(db);
        seed.Category.Name = "Smartphones"; // Source says "Phones": no exact match and no administrator mapping.
        seed.Root.Name = "Devices"; // No matching ancestor either: exercise the manual retry path.
        var kitchen = new Domain.Entities.Category { Id = Guid.NewGuid(), ParentId = seed.Root.Id, Name = "Cookware", Slug = "cookware", IsActive = true };
        db.Categories.Add(kitchen);
        db.SellerCategoryGrants.Add(new() { SellerId = seed.Seller.Id, CategoryId = seed.Root.Id, IncludeDescendants = true });
        await db.SaveChangesAsync();
        var clock = new TelegramTestData.Clock(); var queue = new SellerImportQueue(db, new(db), clock);
        var items = await queue.AddAsync(seed.User.Id, new([new(TelegramTestData.SourceUrl), new("https://rozetka.com.ua/test-pan/p234567/")]), default);
        var runner = Runner(db, clock, new TelegramTestData.Source(), new Bot());
        Assert.True(await runner.ImportOneAsync(default)); Assert.True(await runner.ImportOneAsync(default));
        Assert.All(await queue.ListAsync(seed.User.Id, default), job => Assert.Equal("Failed", job.Status));
        await Assert.ThrowsAsync<BusinessRuleException>(() => queue.RetryAsync(seed.User.Id, items[0].Id, default, Guid.NewGuid()));
        await queue.RetryAsync(seed.User.Id, items[0].Id, default, seed.Category.Id);
        await queue.RetryAsync(seed.User.Id, items[1].Id, default, kitchen.Id);
        Assert.True(await runner.ImportOneAsync(default)); Assert.True(await runner.ImportOneAsync(default));
        var jobs = await queue.ListAsync(seed.User.Id, default);
        Assert.Equal(2, jobs.Count); Assert.All(jobs, job => Assert.Equal("Ready", job.Status));
        foreach (var job in jobs) Assert.Equal(job.CategoryId, (await db.ProductImportDrafts.SingleAsync(d => d.Id == job.PreviewId)).CategoryId);
        Assert.Equal(seed.Category.Id, jobs.Single(j => j.Id == items[0].Id).CategoryId);
        Assert.Equal(kitchen.Id, jobs.Single(j => j.Id == items[1].Id).CategoryId);
        Assert.Empty(await db.Products.ToListAsync()); Assert.Empty(await db.RozetkaCategoryMappings.ToListAsync());
        clock.Now = clock.Now.AddHours(2);
        db.SellerCategoryGrants.RemoveRange(await db.SellerCategoryGrants.ToListAsync()); await db.SaveChangesAsync();
        await Assert.ThrowsAsync<BusinessRuleException>(() => queue.RetryAsync(seed.User.Id, items[0].Id, default, seed.Category.Id));
        var other = await TelegramTestData.SeedAsync(db);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => queue.RetryAsync(other.User.Id, items[0].Id, default, kitchen.Id));
    }

    [Fact]
    public async Task Bulk_RequiresExactPreviewActorScopeAndFreshVersions_AndCannotReplayOrPublish()
    {
        await using var db=TelegramTestData.Memory();var seed=await TelegramTestData.SeedAsync(db);await TelegramTestData.AllowImportAsync(db,seed);
        var product=await Product(db,seed);var clock=new TelegramTestData.Clock();var bulk=new BulkCatalogService(db,new EphemeralDataProtectionProvider(),new(db),clock);
        var preview=await bulk.PreviewAsync(seed.User.Id,seed.User.Id,new(){ProductIds=[product.Id],PricePercent=10,StockQuantity=8},default);
        Assert.Equal(110m,Assert.Single(preview.Rows).AfterPrice);Assert.Equal(100m,(await db.ProductVariants.SingleAsync()).Price);
        await Assert.ThrowsAsync<BusinessRuleException>(()=>bulk.ApplyAsync(Guid.NewGuid(),seed.User.Id,preview.Token,default));
        Assert.Equal(1,await bulk.ApplyAsync(seed.User.Id,seed.User.Id,preview.Token,default));
        Assert.Equal(ProductStatus.DRAFT,(await db.Products.SingleAsync()).Status);Assert.Equal(8,(await db.ProductVariants.SingleAsync()).StockQuantity);
        await Assert.ThrowsAsync<BusinessRuleException>(()=>bulk.ApplyAsync(seed.User.Id,seed.User.Id,preview.Token,default));
        var stale=await bulk.PreviewAsync(seed.User.Id,seed.User.Id,new(){ProductIds=[product.Id],Price=200},default);
        (await db.ProductVariants.SingleAsync()).SetStock(99);await db.SaveChangesAsync();
        await Assert.ThrowsAsync<BusinessRuleException>(()=>bulk.ApplyAsync(seed.User.Id,seed.User.Id,stale.Token,default));
        Assert.Equal(110m,(await db.ProductVariants.SingleAsync()).Price);
    }

    [Fact]
    public async Task Bulk_CannotChangeAnotherSellersProduct_OrProductUnderReview()
    {
        await using var db=TelegramTestData.Memory();var seed=await TelegramTestData.SeedAsync(db);await TelegramTestData.AllowImportAsync(db,seed);var product=await Product(db,seed);
        var bulk=new BulkCatalogService(db,new EphemeralDataProtectionProvider(),new(db),new TelegramTestData.Clock());
        await Assert.ThrowsAsync<KeyNotFoundException>(()=>bulk.PreviewAsync(Guid.NewGuid(),Guid.NewGuid(),new(){ProductIds=[product.Id],Price=123},default));
        (await db.Products.SingleAsync()).Status=ProductStatus.PENDING_MODERATION;await db.SaveChangesAsync();
        await Assert.ThrowsAsync<BusinessRuleException>(()=>bulk.PreviewAsync(seed.User.Id,seed.User.Id,new(){ProductIds=[product.Id],Price=123},default));
    }

    [Fact]
    public async Task DuplicateDetection_UsesBarcodeOrBrandAndManufacturerPartNumber_AndNeverLeaksOtherSellers()
    {
        await using var db=TelegramTestData.Memory();var seed=await TelegramTestData.SeedAsync(db);var product=await Product(db,seed,"EX-123","4820000000001");var duplicates=new CatalogDuplicateService(db);
        Assert.Equal(product.Id,Assert.Single(await duplicates.FindAsync(seed.User.Id,seed.Brand.Id,"ex 123",null,null,default)).Id);
        Assert.Single(await duplicates.FindAsync(seed.User.Id,Guid.NewGuid(),null,"4820000000001",null,default));
        Assert.Empty(await duplicates.FindAsync(Guid.NewGuid(),seed.Brand.Id,"EX-123","4820000000001",null,default));
        Assert.Empty(await duplicates.FindAsync(seed.User.Id,seed.Brand.Id,"EX-123","4820000000001",product.Id,default));
    }

    [Fact]
    public async Task Notifications_AreStoredWithModeration_AndDeliveredToCurrentlyLinkedRolesOnly()
    {
        await using var db=TelegramTestData.Memory();var seed=await TelegramTestData.SeedAsync(db);var clock=new TelegramTestData.Clock();
        var moderator=await TelegramTestData.UserAsync(db,Roles.Moderator);await TelegramTestData.BindAsync(db,moderator,clock,22222);await TelegramTestData.BindAsync(db,seed.User,clock,33333);
        var product=await Product(db,seed);var products=new ProductService(db);
        await products.SubmitForModerationAsync(product.Id);Assert.Equal(moderator.Id,(await db.TelegramNotifications.SingleAsync()).UserId);
        var bot=new Bot();clock.Now=clock.Now.AddSeconds(2);var runner=Runner(db,clock,new TelegramTestData.Source(),bot);
        await runner.DeliverOneAsync(default);Assert.Single(bot.Messages);Assert.Contains("Відкрити товар",JsonDocument.Parse(bot.Messages[0]).RootElement.GetProperty("reply_markup").GetProperty("inline_keyboard")[0][0].GetProperty("text").GetString());
        await products.RejectAsync(product.Id,"Добавьте фото");clock.Now=clock.Now.AddSeconds(2);await runner.DeliverOneAsync(default);
        Assert.Equal(2,bot.Messages.Count);Assert.Contains("Добавьте фото",JsonDocument.Parse(bot.Messages[1]).RootElement.GetProperty("text").GetString());
        await products.SubmitForModerationAsync(product.Id);clock.Now=clock.Now.AddSeconds(2);moderator.Block();await db.SaveChangesAsync();
        await runner.DeliverOneAsync(default);Assert.Equal(2,bot.Messages.Count);
        Assert.Contains(await db.TelegramNotifications.ToListAsync(),n=>n.Result!.StartsWith("Skipped"));
    }

    [Fact]
    public async Task Bulk_ExplainsOldPriceRemoval_AndRejectsAnOldPriceChangedAfterPreview()
    {
        await using var db=TelegramTestData.Memory();var seed=await TelegramTestData.SeedAsync(db);var product=await Product(db,seed);
        var variant=await db.ProductVariants.SingleAsync();
        void ChangeOldPrice(decimal value)=>variant.Update(variant.Sku,variant.Barcode,variant.Name,variant.Price,value,variant.CostPrice,variant.Weight,variant.Length,variant.Width,variant.Height,variant.IsActive);
        ChangeOldPrice(120);await db.SaveChangesAsync();
        var bulk=new BulkCatalogService(db,new EphemeralDataProtectionProvider(),new(db),new TelegramTestData.Clock());
        var preview=await bulk.PreviewAsync(Guid.Empty,null,new(){ProductIds=[product.Id],Price=150},default);
        Assert.Equal(120,Assert.Single(preview.Rows).BeforeOldPrice);Assert.Null(preview.Rows[0].AfterOldPrice);
        ChangeOldPrice(130);await db.SaveChangesAsync();
        await Assert.ThrowsAsync<BusinessRuleException>(()=>bulk.ApplyAsync(Guid.Empty,null,preview.Token,default));
        var fresh=await bulk.PreviewAsync(Guid.Empty,null,new(){ProductIds=[product.Id],Price=150},default);
        await bulk.ApplyAsync(Guid.Empty,null,fresh.Token,default);Assert.Null(variant.OldPrice);
    }

    [Fact]
    public async Task NotificationDelivery_RetriesTemporaryErrors_AndStopsWhenTheBotIsBlocked()
    {
        await using var db=TelegramTestData.Memory();var seed=await TelegramTestData.SeedAsync(db);var clock=new TelegramTestData.Clock();
        var moderator=await TelegramTestData.UserAsync(db,Roles.Moderator);await TelegramTestData.BindAsync(db,moderator,clock,22222);
        var product=await Product(db,seed);await new ProductService(db).SubmitForModerationAsync(product.Id);
        clock.Now=DateTimeOffset.UtcNow.AddMinutes(1);var bot=new Bot{FailureStatus=502};var runner=Runner(db,clock,new TelegramTestData.Source(),bot);
        Assert.True(await runner.DeliverOneAsync(default));var item=await db.TelegramNotifications.SingleAsync();Assert.Null(item.CompletedAt);Assert.Equal(1,item.Attempts);
        Assert.False(await runner.DeliverOneAsync(default));clock.Now=item.AvailableAt.AddSeconds(1);bot.FailureStatus=403;
        Assert.True(await runner.DeliverOneAsync(default));Assert.NotNull(item.CompletedAt);Assert.Equal(2,item.Attempts);
        clock.Now=clock.Now.AddHours(1);Assert.False(await runner.DeliverOneAsync(default));
    }

    private static async Task<CatalogProductDto> Product(ApplicationDbContext db,TelegramTestData.Seed seed,string part="",string? barcode=null)
    {
        var product=await new CatalogService(db).SaveAsync(null,seed.Store.Id,new(){Name="Example product",Slug="example-"+Guid.NewGuid().ToString("N"),CategoryId=seed.Category.Id,BrandId=seed.Brand.Id,StoreId=seed.Store.Id,ManufacturerPartNumber=part,
            Variants=[new(){Name="Main",Sku="SKU-"+Guid.NewGuid().ToString("N"),Barcode=barcode,Price=100,StockQuantity=5}]},default);
        Assert.NotNull(product);return product;
    }
    private static SellerWorkspaceRunner Runner(ApplicationDbContext db,TelegramTestData.Clock clock,IMarketplaceSourceClient source,ITelegramBotClient bot)=>new(db,new(db,source,new(),new(db),new(db),new ProductService(db),new TelegramTestData.Media(),clock),bot,TelegramTestData.Options,clock);
    private sealed class FailingSource:IMarketplaceSourceClient
    {
        public Task<string> GetPageAsync(Uri uri,CancellationToken ct)=>throw new BusinessRuleException("Source unavailable");
        public Task<byte[]> GetImageAsync(Uri uri,CancellationToken ct)=>throw new InvalidOperationException();
    }
    private sealed class Bot:ITelegramBotClient
    {
        internal List<string> Messages=[];
        internal int? FailureStatus;
        public Task<JsonDocument> CallAsync(string method,object payload,CancellationToken ct){if(FailureStatus.HasValue)throw new TelegramBotException("Fixture failure",FailureStatus);Messages.Add(JsonSerializer.Serialize(payload));return Task.FromResult(JsonDocument.Parse("{\"ok\":true,\"result\":{}}"));}
        public Task SendMenuAsync(long chatId,string text,CancellationToken ct)=>throw new InvalidOperationException();
        public Task SendTextAsync(long chatId,string text,CancellationToken ct)=>throw new InvalidOperationException();
    }
}
