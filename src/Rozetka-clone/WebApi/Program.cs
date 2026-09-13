using Application.Products;
using Infrastructure;

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

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();