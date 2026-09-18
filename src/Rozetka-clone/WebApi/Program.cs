using Application.Brands;
using Application.Categories;
using Application.Products;
using Infrastructure;
using Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();

// OpenAPI
builder.Services.AddOpenApi();

// Infrastructure
// PostgreSQL + DbContext + IApplicationDbContext
builder.Services.AddInfrastructure(
    builder.Configuration);

builder.Services.AddScoped<IProductService, ProductService>();
builder.Services.AddScoped<ICategoryService, CategoryService>();
builder.Services.AddScoped<IBrandService, BrandService>();

var app = builder.Build();

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

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
