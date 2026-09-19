using System.Text;
using Application.Attributes;
using Application.Brands;
using Application.Categories;
using Application.ProductAttributeValues;
using Application.ProductImages;
using Application.Products;
using Application.ProductTags;
using Application.ProductVariants;
using Infrastructure;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using WebApi.Extensions;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

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

var jwtSection = builder.Configuration.GetSection("Jwt");
var secretKey = jwtSection["SecretKey"] 
    ?? throw new InvalidOperationException("JWT SecretKey is missing from configuration.");

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
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
});

builder.Services.AddAuthorization();

var app = builder.Build();

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

if (app.Environment.IsDevelopment() &&
    builder.Configuration.GetValue<bool>("SeedDemoUsers"))
{
    try
    {
        var createdUsers = await app.Services.SeedDemoUsersAsync();

        app.Logger.LogInformation(
            "Demo user seed completed. Created entities: {CreatedUsers}.",
            createdUsers);
    }
    catch (Exception exception)
    {
        app.Logger.LogWarning(
            exception,
            "Demo users could not be created because the database is unavailable.");
    }
}

if (app.Environment.IsDevelopment())
{
    app.UseSwaggerDocumentation();
}

app.UseHttpsRedirection();

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

app.MapControllers();

app.Run();