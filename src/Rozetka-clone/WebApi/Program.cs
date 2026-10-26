using Application.Attributes;
using Application.Brands;
using Application.Carts;
using Application.Categories;
using Application.ProductAttributeValues;
using Application.ProductImages;
using Application.Products;
using Application.ProductTags;
using Application.ProductVariants;
using Application.Promotions;
using Application.Reviews;
using Application.Sellers;
using Application.Stores;
using Application.Wishlists;
using Infrastructure;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using WebApi.BackgroundServices;
using WebApi.Extensions;
using WebApi.Telegram;

var deploymentCheck = args.Contains("--check-telegram-deployment", StringComparer.Ordinal);
var operationsCheck = args.Contains("--check-admin-operations-deployment", StringComparer.Ordinal);
var operationsMigrate = args.Contains("--migrate-admin-operations", StringComparer.Ordinal);
var operatorFlags = new[] { "--check-telegram-deployment", "--check-admin-operations-deployment", "--migrate-admin-operations" };
var builder = WebApplication.CreateBuilder(args.Where(argument => !operatorFlags.Contains(argument, StringComparer.Ordinal)).ToArray());

builder.Services.AddScoped<Application.Catalog.CatalogService>();
builder.Services.AddTrebaTelegram(builder.Configuration);
WebApi.Delivery.DeliveryNotificationsRegistration.AddDeliveryNotifications(builder.Services, builder.Configuration);
builder.Services.AddScoped<Application.Administration.AdminWorkspaceService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<Application.Abstractions.ICurrentActor, WebApi.Authentication.HttpCurrentActor>();
builder.Services.AddScoped<Infrastructure.Administration.StaffSettingsService>();
builder.Services.AddOptions<WebApi.Media.ProductMediaOptions>().BindConfiguration("ProductMedia")
    .Validate(options => options.Provider is "Local" or "Sftp", "ProductMedia:Provider must be Local or Sftp.")
    .Validate(options => options.Provider != "Sftp" || (!string.IsNullOrWhiteSpace(options.SshHost)
        && !string.IsNullOrWhiteSpace(options.SshUsername) && !string.IsNullOrWhiteSpace(options.SshKeyPath)
        && options.RootPath.StartsWith('/') && options.SshPort is > 0 and <= 65535), "Shared media connection is incomplete.")
    .ValidateOnStart();
builder.Services.AddSingleton<Application.Abstractions.IProductMediaStorage, WebApi.Media.ProductMediaStorage>();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<WebApi.Errors.ApiExceptionHandler>();

builder.Services.AddControllers();

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        policy
            .WithOrigins("http://localhost:3000")
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

builder.Services.AddSwaggerDocumentation();

builder.Services.AddInfrastructure(builder.Configuration, builder.Environment);

builder.Services.AddScoped<IProductService, ProductService>();
builder.Services.AddScoped<ICategoryService, CategoryService>();
builder.Services.AddScoped<IBrandService, BrandService>();
builder.Services.AddScoped<IAttributeService, AttributeService>();
builder.Services.AddScoped<IProductVariantService, ProductVariantService>();
builder.Services.AddScoped<IProductImageService, ProductImageService>();
builder.Services.AddScoped<IProductAttributeValueService, ProductAttributeValueService>();
builder.Services.AddScoped<IProductTagService, ProductTagService>();
builder.Services.AddScoped<ISellerService, SellerService>();
builder.Services.AddScoped<IStoreService, StoreService>();
builder.Services.AddScoped<ICartService, CartService>();
builder.Services.AddScoped<IWishlistService, WishlistService>();
builder.Services.AddScoped<IReviewService, ReviewService>();
builder.Services.AddScoped<IDiscountService, DiscountService>();
builder.Services.AddScoped<Application.Inventory.InventoryService>();
builder.Services.AddScoped<Application.Inventory.IInventoryService>(sp => sp.GetRequiredService<Application.Inventory.InventoryService>());
Application.Orders.OrderRegistration.AddOrderServices(builder.Services);
builder.Services.AddScoped<Application.Administration.PromotionManagementService>();
builder.Services.AddScoped<Application.Administration.SupportService>();
builder.Services.AddScoped<Application.Administration.FeatureSettingsService>();
builder.Services.AddScoped<Application.Administration.OperationsService>();
builder.Services.AddScoped<Application.Marketplace.SellerApplicationService>();
builder.Services.AddScoped<Application.Marketplace.CustomerAccountService>();
builder.Services.AddScoped<Application.Marketplace.NotificationService>();
builder.Services.AddHostedService<OrderReservationWorker>();


builder.Services.AddHostedService<PromotionActivationWorker>();

var jwtSection = builder.Configuration.GetSection("Jwt");

builder
    .Services
    .AddAuthentication(
        options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        }
    )
    .AddJwtBearer(
        options =>
        {
            var secretKey =
                jwtSection["SecretKey"]
                ?? throw new InvalidOperationException("JWT SecretKey is missing from configuration.");
            if (Encoding.UTF8.GetByteCount(secretKey) < 32)
            {
                throw new InvalidOperationException("JWT SecretKey must be at least 32 bytes.");
            }

            options.RequireHttpsMetadata = false;
            options.SaveToken = true;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = jwtSection["Issuer"],
                ValidAudience = jwtSection["Audience"],
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey)),
                ClockSkew = TimeSpan.Zero,
            };
            options.Events = new JwtBearerEvents
            {
                OnTokenValidated = async context =>
                {
                    var idClaim = context.Principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                    if (!Guid.TryParse(
                        idClaim,
                        out var id
                    ))
                    {
                        context.Fail("Invalid user.");
                        return;
                    }
                    var db = context.HttpContext.RequestServices.GetRequiredService<ApplicationDbContext>();
                    var user = await db
                        .Users
                        .AsNoTracking()
                        .Include(u => u.Role)
                        .SingleOrDefaultAsync(
                            u => u.Id == id,
                            context.HttpContext.RequestAborted
                        );
                    if (
                        user is null
                        || user.Status
                            is Domain.Entities.Users.UserStatus.Blocked
                                or Domain.Entities.Users.UserStatus.Deleted
                        || user.Role?.Name != context.Principal?.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value
                        || !Infrastructure.Authentication.SessionTokenBinding.IsCurrent(
                            context.Principal?.FindFirst(Infrastructure.Authentication.SessionTokenBinding.ClaimName)?.Value, user)
                    )
                    {
                        context.Fail("Account is unavailable or role has changed.");
                    }
                },
            };
        }
    );

builder.Services.AddAuthorization();

var app = builder.Build();

app.UseExceptionHandler();
if (operationsCheck || operationsMigrate)
{
    Environment.ExitCode = await WebApi.Administration.AdminOperationsDeployment.RunAsync(app.Services, operationsMigrate);
    await app.DisposeAsync();
    return;
}
if (deploymentCheck)
{
    Environment.ExitCode = await TelegramDeploymentCheck.RunAsync(app.Services,
        builder.Configuration.GetValue<bool>("verify-telegram-menu"));
    await app.DisposeAsync();
    return;
}
if (builder.Configuration.GetValue<bool>("ApplyMigrations"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.MigrateAsync();
}

try
{
    await app.Services.SeedRolesAsync();
    app.Logger.LogInformation("System roles seed completed successfully.");
}
catch (Exception exception)
{
    app.Logger.LogWarning(
        exception,
        "Roles could not be seeded because the database is unavailable."
    );
}

await app.Services.SeedAdministratorAsync(builder.Configuration);

if (app.Environment.IsDevelopment())
{
    app.UseSwaggerDocumentation();
}

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseCors("Frontend");
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapGet(
    "/health",
    async (
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken
    ) =>
    {
        var databaseOk = await dbContext.Database.CanConnectAsync(cancellationToken);

        return databaseOk
            ? Results.Ok(
                new
                {
                    status = "ok",
                    database = "ok"
                }
            )
            : Results.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Database unavailable"
            );
    }
);

// Lightweight endpoint for Docker/CI smoke tests.
// Does not require a database connection.
app.MapGet(
    "/ping",
    () =>
        Results.Ok(
            new
            {
                status = "ok"
            }
        )
);

app.MapControllers();
Treba.Development.LocalLauncherEndpoints.Map(app);
TelegramPreviewEndpoints.Map(app);

app.Run();
