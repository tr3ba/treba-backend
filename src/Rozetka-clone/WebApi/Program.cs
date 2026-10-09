using Application.Attributes;
using Application.Brands;
using Application.Carts;
using Application.Categories;
using Application.ProductAttributeValues;
using Application.ProductImages;
using Application.Products;
using Application.ProductTags;
using Application.ProductVariants;
using Application.Sellers;
using Application.Stores;
using Infrastructure;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using WebApi.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddScoped<Application.Catalog.CatalogService>();
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

builder.Services.AddInfrastructure(builder.Configuration);

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

var jwtSection = builder.Configuration.GetSection("Jwt");

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    var secretKey = jwtSection["SecretKey"]
        ?? throw new InvalidOperationException("JWT SecretKey is missing from configuration.");
    if (Encoding.UTF8.GetByteCount(secretKey) < 32)
        throw new InvalidOperationException("JWT SecretKey must be at least 32 bytes.");
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
        ClockSkew = TimeSpan.Zero
    };
    options.Events = new JwtBearerEvents
    {
        OnTokenValidated = async context =>
        {
            var idClaim = context.Principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(idClaim, out var id)) { context.Fail("Invalid user."); return; }
            var db = context.HttpContext.RequestServices.GetRequiredService<ApplicationDbContext>();
            var user = await db.Users.AsNoTracking().Include(u => u.Role).SingleOrDefaultAsync(u => u.Id == id, context.HttpContext.RequestAborted);
            if (user is null || user.Status is Domain.Entities.Users.UserStatus.Blocked or Domain.Entities.Users.UserStatus.Deleted
                || user.Role?.Name != context.Principal?.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value)
                context.Fail("Account is unavailable or role has changed.");
        }
    };
});

builder.Services.AddAuthorization();

var app = builder.Build();

app.UseExceptionHandler();
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
        "Roles could not be seeded because the database is unavailable.");
}

await app.Services.SeedAdministratorAsync(builder.Configuration);

if (app.Environment.IsDevelopment())
{
    app.UseSwaggerDocumentation();
}

if (!app.Environment.IsDevelopment())
    app.UseHttpsRedirection();

app.UseStaticFiles();
app.UseCors("Frontend");
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", async (
    ApplicationDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var databaseOk = await dbContext.Database.CanConnectAsync(cancellationToken);

    return databaseOk
        ? Results.Ok(new
        {
            status = "ok",
            database = "ok"
        })
        : Results.Problem(
            statusCode: StatusCodes.Status503ServiceUnavailable,
            title: "Database unavailable");
});

// Lightweight endpoint for Docker/CI smoke tests.
// Does not require a database connection.
app.MapGet("/ping", () => Results.Ok(new
{
    status = "ok"
}));

app.MapControllers();

app.Run();
