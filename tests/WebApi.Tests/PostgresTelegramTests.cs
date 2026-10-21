using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;
using Contracts.Telegram;
using Microsoft.AspNetCore.Mvc;
using WebApi.Controllers;
using WebApi.Telegram;

namespace WebApi.Tests;

public sealed class PostgresTelegramTests
{
    [PostgresFact]
    public async Task ConcurrentCatalogDeployment_AddsCookwareAndMappingOnlyOnce()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(Environment.GetEnvironmentVariable("TREBA_TEST_POSTGRES")!).ReplaceService<IModelCustomizer, Model>().Options;
        await using var sql = new NpgsqlConnection(Environment.GetEnvironmentVariable("TREBA_TEST_POSTGRES")!); await sql.OpenAsync();
        string[] tables = [];
        try
        {
            await using (var db = new ApplicationDbContext(options))
            {
                tables = db.Model.GetEntityTypes().Select(e => e.GetTableName()!).Distinct().ToArray();
                await PostgresTestTables.CreateAsync(db, sql, Prefix);
                db.Categories.Add(new() { Id = Guid.NewGuid(), Name = "Для приготування їжі", Slug = WebApi.Administration.ImportCatalogDeployment.CookwareSlug, IsActive = true });
                await db.SaveChangesAsync();
            }
            async Task Apply()
            {
                await using var db = new ApplicationDbContext(options);
                await WebApi.Administration.ImportCatalogDeployment.ApplyAsync(db, default);
            }
            await Task.WhenAll(Apply(), Apply());
            await using var check = new ApplicationDbContext(options);
            Assert.Equal(9, await check.Categories.CountAsync());
            var mapping = await check.RozetkaCategoryMappings.SingleAsync();
            Assert.Equal("c4626754", mapping.ExternalKey);
            Assert.Equal("Сковороди", (await check.Categories.SingleAsync(c => c.Id == mapping.CategoryId)).Name);
        }
        finally
        {
            await using var owned = new NpgsqlCommand("SELECT tablename FROM pg_tables WHERE schemaname='public' AND tableowner=current_user AND tablename=ANY(@names)", sql);
            owned.Parameters.AddWithValue("names", tables); var names = new List<string>();
            await using (var reader = await owned.ExecuteReaderAsync()) { while (await reader.ReadAsync()) names.Add(reader.GetString(0)); }
            if (names.Count > 0)
            {
                Assert.All(names, n => Assert.StartsWith(Prefix, n));
                await using var drop = new NpgsqlCommand("DROP TABLE " + string.Join(',', names.Select(n => "public.\"" + n.Replace("\"", "\"\"") + "\"")) + " CASCADE", sql);
                await drop.ExecuteNonQueryAsync();
            }
        }
    }

    private static readonly string Prefix="treba_qa_tg_"+Guid.NewGuid().ToString("N")[..10]+"_";
    public sealed class Model(ModelCustomizerDependencies dependencies):ModelCustomizer(dependencies)
    {
        public override void Customize(ModelBuilder builder,DbContext context)
        {
            base.Customize(builder,context);
            foreach(var entity in builder.Model.GetEntityTypes())
                if(entity.GetTableName() is {} table){entity.SetTableName(Prefix+table);entity.SetSchema("public");}
        }
    }
    [PostgresFact]
    public async Task ConcurrentImportBrandCreation_ReusesOneBrand_AndReturnsItInThePreview()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(Environment.GetEnvironmentVariable("TREBA_TEST_POSTGRES")!).ReplaceService<IModelCustomizer, Model>().Options;
        await using var sql = new NpgsqlConnection(Environment.GetEnvironmentVariable("TREBA_TEST_POSTGRES")!); await sql.OpenAsync();
        string[] tables = [];
        try
        {
            Guid userId, previewId;
            var clock = new TelegramTestData.Clock();
            await using(var db = new ApplicationDbContext(options))
            {
                tables = db.Model.GetEntityTypes().Select(e => e.GetTableName()!).Distinct().ToArray();
                await PostgresTestTables.CreateAsync(db, sql, Prefix);
                var seed = await TelegramTestData.SeedAsync(db); await TelegramTestData.AllowImportAsync(db, seed); userId = seed.User.Id;
                previewId = (await TelegramTestData.Import(db, clock, new(), new()).PreviewAsync(userId, TelegramTestData.SourceUrl, default)).Id;
                (await db.ProductImportDrafts.SingleAsync()).BrandName = " Acme   Labs "; await db.SaveChangesAsync();
            }
            async Task<Guid> Create(string name)
            {
                await using var db = new ApplicationDbContext(options);
                return (await TelegramTestData.Import(db, clock, new(), new()).CreateBrandAsync(userId, previewId, name, default)).Id;
            }
            var created = await Task.WhenAll(Create("Acme Labs"), Create(" ACME  LABS "), Create("acme labs"));
            Assert.Single(created.Distinct());
            await using(var db = new ApplicationDbContext(options))
            {
                Assert.Equal(2, await db.Brands.CountAsync());
                Assert.Equal(created[0], (await TelegramTestData.Import(db, clock, new(), new()).GetAsync(userId, previewId, default)).BrandId);
            }
        }
        finally
        {
            await using var owned = new NpgsqlCommand("SELECT tablename FROM pg_tables WHERE schemaname='public' AND tableowner=current_user AND tablename=ANY(@names)", sql);
            owned.Parameters.AddWithValue("names", tables); var names = new List<string>();
            await using(var reader = await owned.ExecuteReaderAsync()) while(await reader.ReadAsync()) names.Add(reader.GetString(0));
            if(names.Count > 0)
            {
                Assert.All(names, name => Assert.StartsWith(Prefix, name));
                await using var drop = new NpgsqlCommand("DROP TABLE " + string.Join(',', names.Select(name => "public.\"" + name.Replace("\"", "\"\"") + "\"")) + " CASCADE", sql);
                await drop.ExecuteNonQueryAsync();
            }
        }
    }
    [PostgresFact]
    public async Task PostgreSql_GuardConcurrency_AndImportPhotoFailureRollsBackAllRowsAndFiles()
    {
        var connection=Environment.GetEnvironmentVariable("TREBA_TEST_POSTGRES")!;
        var options=new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connection).ReplaceService<IModelCustomizer,Model>().Options;
        await using var sql=new NpgsqlConnection(connection);await sql.OpenAsync();
        async Task Execute(string text){await using var cmd=new NpgsqlCommand(text,sql);await cmd.ExecuteNonQueryAsync();}
        string[] tables=[];
        try {
            await using(var db=new ApplicationDbContext(options)) {
                tables=db.Model.GetEntityTypes().Select(e=>e.GetTableName()!).Distinct().ToArray();
                Assert.All(tables,t=>Assert.StartsWith(Prefix,t));await Execute(db.Database.GenerateCreateScript());
            }
            var clock=new TelegramTestData.Clock();TelegramTestData.Seed seed;Guid challengeId;string browserSecret;
            await using(var db=new ApplicationDbContext(options)) {
                seed=await TelegramTestData.SeedAsync(db);await TelegramTestData.AllowImportAsync(db,seed);await TelegramTestData.BindAsync(db,seed.User,clock);
                var settings=new TelegramSettingsController(db);
                Assert.Equal("c80001",Assert.Single(await settings.Mappings(default)).ExternalKey);
                var grants=Assert.IsType<OkObjectResult>(await settings.SellerCategories(seed.Seller.Id,default));
                Assert.Equal(seed.Root.Id,Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<SellerCategoryGrantDto>>(grants.Value)).CategoryId);
                var guard=new TelegramGuardService(db,new(db,TelegramTestData.Options,clock),TelegramTestData.Identity(db),TelegramTestData.Options,clock);
                var start=await guard.CreateAsync("PostgreSQL test",null,default);await guard.ApproveAsync(start.QrPayload,seed.User.Id,12345,default);
                challengeId=start.Id;browserSecret=start.BrowserSecret;
            }
            await using(var first=new ApplicationDbContext(options))
            await using(var stale=new ApplicationDbContext(options)) {
                await stale.TelegramGuardChallenges.SingleAsync(c=>c.Id==challengeId);
                var guard=new TelegramGuardService(first,new(first,TelegramTestData.Options,clock),TelegramTestData.Identity(first),TelegramTestData.Options,clock);
                Assert.NotNull((await guard.CompleteAsync(challengeId,browserSecret,default)).Authentication);
                var staleGuard=new TelegramGuardService(stale,new(stale,TelegramTestData.Options,clock),TelegramTestData.Identity(stale),TelegramTestData.Options,clock);
                await Assert.ThrowsAsync<DbUpdateConcurrencyException>(()=>staleGuard.CompleteAsync(challengeId,browserSecret,default));
            }
            var source=new TelegramTestData.Source();var media=new TelegramTestData.Media {Fail=true};Guid previewId;
            await using(var db=new ApplicationDbContext(options)) {
                var importer=TelegramTestData.Import(db,clock,source,media);previewId=(await importer.PreviewAsync(seed.User.Id,TelegramTestData.SourceUrl,default)).Id;
                await Assert.ThrowsAsync<IOException>(()=>importer.SubmitAsync(seed.User.Id,previewId,TelegramTestData.Submit(seed),default));
            }
            Assert.Empty(media.Files);
            await using(var db=new ApplicationDbContext(options)) {
                Assert.Empty(await db.Products.ToListAsync());Assert.Empty(await db.ProductImages.ToListAsync());Assert.Empty(await db.ProductVariants.ToListAsync());
                Assert.Null((await db.ProductImportDrafts.SingleAsync()).SubmittedAt);
                media.Fail=false;var importer=TelegramTestData.Import(db,clock,source,media);
                var productId=await importer.SubmitAsync(seed.User.Id,previewId,TelegramTestData.Submit(seed),default);
                Assert.Equal(productId,await importer.SubmitAsync(seed.User.Id,previewId,TelegramTestData.Submit(seed),default));
                Assert.Single(await db.Products.ToListAsync());Assert.Single(await db.ProductImages.ToListAsync());
            }
        }
        finally {
            // Only this test's tables owned by the current database user may be removed.
            await using var owned=new NpgsqlCommand("SELECT tablename FROM pg_tables WHERE schemaname='public' AND tableowner=current_user AND tablename=ANY(@names)",sql);
            owned.Parameters.AddWithValue("names",tables);var names=new List<string>();
            await using(var reader=await owned.ExecuteReaderAsync()){while(await reader.ReadAsync())names.Add(reader.GetString(0));}
            if(names.Count>0){Assert.All(names,n=>Assert.StartsWith(Prefix,n));await Execute("DROP TABLE "+string.Join(',',names.Select(n=>"public.\""+n.Replace("\"","\"\"")+"\""))+" CASCADE");}
        }
    }
}
