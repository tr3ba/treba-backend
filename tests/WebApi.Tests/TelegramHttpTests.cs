using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Application.Abstractions;
using Application.Administration;
using Application.Catalog;
using Application.Products;
using Application.Reviews;
using Contracts.Telegram;
using Domain.Entities.Product;
using Domain.Entities.Users;
using Domain.Enums;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WebApi.Controllers;
using WebApi.Errors;
using WebApi.Telegram;

namespace WebApi.Tests;

public sealed class TelegramHttpTests
{
    [Fact]
    public async Task Hosting_RedirectsAnAccidentallyCopiedMessageTime_AndKeepsOtherMissingPathsMissing()
    {
        var webRoot = Path.Combine(Path.GetTempPath(), "treba-telegram-http-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(webRoot, "telegram"));
        await File.WriteAllTextAsync(Path.Combine(webRoot, "telegram", "index.html"), "<div class='app-shell'>Treba</div>");
        await File.WriteAllTextAsync(Path.Combine(webRoot, "telegram", "app.js"), "const app = window.Telegram;");
        try
        {
            await using var server = await ServerAsync(webRoot);
            using var client = server.GetTestClient();
            using var redirected = await client.GetAsync("/telegram/13:37");
            Assert.Equal(HttpStatusCode.Redirect, redirected.StatusCode);
            Assert.Equal("/telegram/", redirected.Headers.Location?.OriginalString);
            Assert.Contains("app-shell", await client.GetStringAsync("/telegram/"));
            Assert.Contains("window.Telegram", await client.GetStringAsync("/telegram/app.js"));
            using var missing = await client.GetAsync("/telegram/missing.js");
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        }
        finally
        {
            Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()), Path.GetFullPath(webRoot));
            Directory.Delete(webRoot, recursive: true);
        }
    }

    private static async Task<WebApplication> ServerAsync(string? webRoot = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { WebRootPath = webRoot }); builder.WebHost.UseTestServer(); builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string,string?> { ["Telegram:Enabled"]="true", ["Telegram:BotToken"]=TelegramTestData.BotToken,
            ["Telegram:BotUsername"]="TrebaTestBot", ["Telegram:MiniAppUrl"]="https://treba.example/telegram/" });
        var services = builder.Services; var database = Guid.NewGuid().ToString();
        services.AddDbContext<ApplicationDbContext>(o => o.UseInMemoryDatabase(database));
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());
        services.AddSingleton<TimeProvider>(new TelegramTestData.Clock());
        services.AddTrebaTelegram(builder.Configuration);
        services.AddSingleton<IMarketplaceSourceClient, TelegramTestData.Source>(); services.AddSingleton<IProductMediaStorage, TelegramTestData.Media>();
        services.AddScoped<IIdentityService>(sp => TelegramTestData.Identity(sp.GetRequiredService<ApplicationDbContext>()));
        services.AddScoped<CatalogService>(); services.AddScoped<IProductService,ProductService>(); services.AddScoped<AdminWorkspaceService>(); services.AddScoped<IReviewService, ReviewService>();
        services.AddScoped<Application.Sellers.ISellerService,Application.Sellers.SellerService>();
        services.AddAuthentication("Bearer").AddScheme<AuthenticationSchemeOptions, WebsiteAuthentication>("Bearer", _ => {});
        services.AddAuthorization(); services.AddControllers().AddApplicationPart(typeof(TelegramMiniAppController).Assembly);
        services.AddProblemDetails(); services.AddExceptionHandler<ApiExceptionHandler>();
        var app = builder.Build(); app.UseExceptionHandler(); app.UseRouting();
        if (webRoot is not null) { app.UseDefaultFiles(); app.UseStaticFiles(); }
        app.UseAuthentication(); app.UseAuthorization(); app.UseRateLimiter(); app.MapControllers();
        await app.StartAsync(); return app;
    }
    private sealed class WebsiteAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options,logger,encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Website-Role",out var role)) return Task.FromResult(AuthenticateResult.NoResult());
            var claims = new[] { new Claim(ClaimTypes.Role,role.ToString()), new Claim(ClaimTypes.NameIdentifier,Request.Headers["X-Website-User"].ToString()) };
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims,Scheme.Name)),Scheme.Name)));
        }
    }

    [Theory]
    [InlineData(Roles.Seller, HttpStatusCode.OK)] [InlineData(Roles.Moderator, HttpStatusCode.OK)]
    [InlineData(Roles.Customer, HttpStatusCode.Unauthorized)] [InlineData(Roles.Administrator, HttpStatusCode.Unauthorized)]
    public async Task MiniApp_RequiresSignedTelegramData_AndCurrentEligibleBinding(string role, HttpStatusCode expected)
    {
        await using var server = await ServerAsync(); using var client = server.GetTestClient(); Guid userId;
        var clock = (TelegramTestData.Clock)server.Services.GetRequiredService<TimeProvider>();
        using (var scope = server.Services.CreateScope()) {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(); var user = await TelegramTestData.UserAsync(db,role); userId=user.Id;
            db.TelegramBindings.Add(new() { UserId=user.Id,TelegramUserId=12345,ChatId=12345,LinkedAt=clock.Now }); await db.SaveChangesAsync();
        }
        client.DefaultRequestHeaders.Add("X-Website-Role",role); client.DefaultRequestHeaders.Add("X-Website-User",userId.ToString());
        Assert.Equal(HttpStatusCode.Unauthorized,(await client.GetAsync("/api/telegram/mini/bootstrap")).StatusCode);
        client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Tma",TelegramTestData.Signed(12345,clock.Now));
        using var bootstrap = await client.GetAsync("/api/telegram/mini/bootstrap"); Assert.Equal(expected,bootstrap.StatusCode);
        if (expected!=HttpStatusCode.OK) return;
        Assert.True(bootstrap.Headers.CacheControl?.NoStore);
        using (var scope = server.Services.CreateScope()) {
            var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(); db.Remove(await db.TelegramBindings.SingleAsync()); await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Unauthorized,(await client.GetAsync("/api/telegram/mini/bootstrap")).StatusCode);
    }

    [Fact]
    public async Task MiniApp_ScopesSellers_AndSeparatesModeratorMutations()
    {
        await using var server=await ServerAsync(); using var client=server.GetTestClient(); Guid ownId,otherId;
        var clock=(TelegramTestData.Clock)server.Services.GetRequiredService<TimeProvider>();
        using(var scope=server.Services.CreateScope()) {
            var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(); var seed=await TelegramTestData.SeedAsync(db);
            await TelegramTestData.BindAsync(db,seed.User,clock);
            var moderator=await TelegramTestData.UserAsync(db,Roles.Moderator); await TelegramTestData.BindAsync(db,moderator,clock,67890);
            var own=new Product {Id=Guid.NewGuid(),StoreId=seed.Store.Id,CategoryId=seed.Category.Id,BrandId=seed.Brand.Id,Name="Own",Slug="own",Status=ProductStatus.DRAFT};
            var other=new Product {Id=Guid.NewGuid(),StoreId=Guid.NewGuid(),CategoryId=seed.Category.Id,BrandId=seed.Brand.Id,Name="Other",Slug="other",Status=ProductStatus.PENDING_MODERATION};
            db.Products.AddRange(own,other);await db.SaveChangesAsync();ownId=own.Id;otherId=other.Id;
        }
        client.DefaultRequestHeaders.Authorization=new("Tma",TelegramTestData.Signed(12345,clock.Now));
        Assert.Equal(HttpStatusCode.OK,(await client.GetAsync($"/api/telegram/mini/products/{ownId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await client.GetAsync($"/api/telegram/mini/products/{otherId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await client.PostAsync($"/api/telegram/mini/products/{otherId}/approve",null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await client.GetAsync("/api/telegram/mini/reviews")).StatusCode);
        client.DefaultRequestHeaders.Authorization=new("Tma",TelegramTestData.Signed(67890,clock.Now));
        Assert.Equal(HttpStatusCode.OK,(await client.GetAsync($"/api/telegram/mini/products/{otherId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await client.PostAsJsonAsync("/api/telegram/mini/products",new {})).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await client.PostAsJsonAsync("/api/telegram/mini/imports",new ImportUrlRequest(TelegramTestData.SourceUrl))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,(await client.PostAsync($"/api/telegram/mini/products/{otherId}/approve",null)).StatusCode);
    }

    [Fact]
    public async Task Bot_StartGreetsWithRoleAndIds_AndWaitsForWebsiteConfirmation()
    {
        await using var server=await ServerAsync(); var clock=(TelegramTestData.Clock)server.Services.GetRequiredService<TimeProvider>();
        var bot=new Bot(); var worker=new TelegramBotWorker(server.Services.GetRequiredService<IServiceScopeFactory>(),TelegramTestData.Options,bot,
            server.Services.GetRequiredService<ILogger<TelegramBotWorker>>(),clock); TelegramLinkStartDto start;Guid userId;
        using(var scope=server.Services.CreateScope()) {
            var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();var user=await TelegramTestData.UserAsync(db);userId=user.Id;
            start=await scope.ServiceProvider.GetRequiredService<TelegramLinkService>().BeginAsync(user.Id,default);
        }
        JsonElement Update(string text,string type="private")=>JsonSerializer.SerializeToElement(new {message=new {text,from=new {id=12345,is_bot=false,first_name="Telegram",username="test"},chat=new {id=12345,type}}});
        await worker.HandleUpdateAsync(Update("/start link_"+start.BotUrl.Split("link_")[1],"group"),default); Assert.Empty(bot.Messages);
        await worker.HandleUpdateAsync(Update("/start link_"+start.BotUrl.Split("link_")[1]),default);
        Assert.Contains("Продавец",Assert.Single(bot.Messages));Assert.Contains(userId.ToString(),bot.Messages[0]);Assert.Contains("12345",bot.Messages[0]);Assert.Equal(0,bot.Menus);
        using(var scope=server.Services.CreateScope()) {
            var links=scope.ServiceProvider.GetRequiredService<TelegramLinkService>();Assert.False((await links.StatusAsync(userId,default)).Linked);
            await links.ConfirmAsync(userId,start.Id,default);
        }
        await worker.HandleUpdateAsync(Update("/menu"),default);Assert.Equal(1,bot.Menus);
    }

    [Theory]
    [InlineData(Roles.Administrator,HttpStatusCode.OK)] [InlineData(Roles.Manager,HttpStatusCode.Forbidden)]
    [InlineData(Roles.Moderator,HttpStatusCode.Forbidden)] [InlineData(Roles.Seller,HttpStatusCode.Forbidden)]
    public async Task TelegramSettings_RequireWebsiteAdministrator(string role,HttpStatusCode expected)
    {
        await using var server=await ServerAsync();using var client=server.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Website-Role",role);client.DefaultRequestHeaders.Add("X-Website-User",Guid.NewGuid().ToString());
        Assert.Equal(expected,(await client.GetAsync("/api/v1/admin/telegram/mappings")).StatusCode);
    }
    [Theory]
    [InlineData(Roles.Administrator,HttpStatusCode.Created)]
    [InlineData(Roles.Manager,HttpStatusCode.Forbidden)]
    [InlineData(Roles.Moderator,HttpStatusCode.Forbidden)]
    [InlineData(Roles.Seller,HttpStatusCode.Forbidden)]
    [InlineData(Roles.Customer,HttpStatusCode.Forbidden)]
    public async Task SellerWizard_IsAnAdministratorOnlyAtomicEndpoint(string role,HttpStatusCode expected)
    {
        await using var server=await ServerAsync();using var client=server.GetTestClient();
        using(var scope=server.Services.CreateScope()) await TelegramTestData.SeedAsync(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
        client.DefaultRequestHeaders.Add("X-Website-Role",role);client.DefaultRequestHeaders.Add("X-Website-User",Guid.NewGuid().ToString());
        var response=await client.PostAsJsonAsync("/api/v1/admin/sellers",new CreateSellerWorkspaceRequest { Email="wizard@example.test",Password="Workspace-password-123!",FirstName="Test",LastName="Seller",CompanyName="Test company",StoreName="Test store",StoreSlug="wizard-store",TestSeller=true,AllCategories=true });
        Assert.Equal(expected,response.StatusCode);
        if(expected==HttpStatusCode.Created){Assert.NotNull(response.Headers.Location);Assert.DoesNotContain("Workspace-password",await response.Content.ReadAsStringAsync());}
    }

    [Fact]
    public async Task Workspace_MiniAppQueueAndBulk_RejectModeratorAndCustomerMutations()
    {
        await using var server=await ServerAsync();using var client=server.GetTestClient();
        var clock=(TelegramTestData.Clock)server.Services.GetRequiredService<TimeProvider>();Guid sellerId;
        using(var scope=server.Services.CreateScope()){
            var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();var seed=await TelegramTestData.SeedAsync(db);await TelegramTestData.AllowImportAsync(db,seed);sellerId=seed.User.Id;await TelegramTestData.BindAsync(db,seed.User,clock);
            var moderator=await TelegramTestData.UserAsync(db,Roles.Moderator);await TelegramTestData.BindAsync(db,moderator,clock,67890);
        }
        client.DefaultRequestHeaders.Authorization=new("Tma",TelegramTestData.Signed(67890,clock.Now));
        Assert.Equal(HttpStatusCode.Forbidden,(await client.PostAsJsonAsync("/api/telegram/mini/imports/queue",new QueueImportsRequest([new(TelegramTestData.SourceUrl)]))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await client.PostAsJsonAsync("/api/telegram/mini/bulk/preview",new{})).StatusCode);
        client.DefaultRequestHeaders.Authorization=new("Tma",TelegramTestData.Signed(12345,clock.Now));
        Assert.Equal(HttpStatusCode.OK,(await client.PostAsJsonAsync("/api/telegram/mini/imports/queue",new QueueImportsRequest([new(TelegramTestData.SourceUrl)]))).StatusCode);
        using(var scope=server.Services.CreateScope())Assert.Equal(sellerId,(await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().SellerImportJobs.SingleAsync()).UserId);
        client.DefaultRequestHeaders.Authorization=null;client.DefaultRequestHeaders.Add("X-Website-Role",Roles.Customer);client.DefaultRequestHeaders.Add("X-Website-User",sellerId.ToString());
        Assert.Equal(HttpStatusCode.Forbidden,(await client.PostAsJsonAsync("/api/v1/admin/catalog/bulk/preview",new{})).StatusCode);
    }
    private sealed class Bot : ITelegramBotClient
    {
        internal List<string> Messages=[];internal int Menus;
        public Task<JsonDocument> CallAsync(string method,object payload,CancellationToken ct)=>throw new NotSupportedException();
        public Task SendTextAsync(long chatId,string text,CancellationToken ct){Messages.Add(text);return Task.CompletedTask;}
        public Task SendMenuAsync(long chatId,string text,CancellationToken ct){Menus++;Messages.Add(text);return Task.CompletedTask;}
    }
}
