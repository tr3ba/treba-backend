using Application.Products;
using Infrastructure;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

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