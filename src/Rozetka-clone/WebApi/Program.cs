using System.Text;
using Application.Products;
using Infrastructure;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using WebApi.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

builder.Services.AddSwaggerDocumentation();

builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddScoped<IProductService, ProductService>();

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

app.MapControllers();

app.Run();