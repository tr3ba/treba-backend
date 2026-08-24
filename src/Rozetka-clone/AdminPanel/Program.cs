using AdminPanel.Auth;
using AdminPanel.Services.Abstractions;
using AdminPanel.Services.Implementations;
using AdminPanel.State;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Localization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddLocalization();

builder.Services.AddScoped<TokenStorageService>();
builder.Services.AddScoped<AuthorizedHttpMessageHandler>();

builder.Services.AddScoped<AuthenticationStateProvider, JwtAuthenticationStateProvider>();
builder.Services.AddScoped<JwtAuthenticationStateProvider>(sp => 
    (JwtAuthenticationStateProvider)sp.GetRequiredService<AuthenticationStateProvider>());

builder.Services.AddScoped(sp =>
{
    var handler = sp.GetRequiredService<AuthorizedHttpMessageHandler>();
    var client = new HttpClient(handler)
    {
        BaseAddress = new Uri(builder.Configuration["ApiBaseUrl"] ?? "https://localhost:7001/")
    };
    return client;
});

builder.Services.AddScoped<IUsersApiClient, UsersApiClient>();
builder.Services.AddScoped<ISellersApiClient, SellersApiClient>();
builder.Services.AddScoped<IOrdersApiClient, OrdersApiClient>();
builder.Services.AddScoped<IProductsApiClient, ProductsApiClient>();
builder.Services.AddScoped<ICategoriesApiClient, CategoriesApiClient>();

builder.Services.AddScoped<UsersState>();
builder.Services.AddScoped<SellersState>();
builder.Services.AddScoped<OrdersState>();
builder.Services.AddScoped<ProductsState>();
builder.Services.AddScoped<CategoriesState>();

var app = builder.Build();

var supportedCultures = new[] { "uk-UA", "uk", "en-US", "en", "ru-RU", "ru" };
var localizationOptions = new RequestLocalizationOptions()
    .SetDefaultCulture("uk-UA")
    .AddSupportedCultures(supportedCultures)
    .AddSupportedUICultures(supportedCultures);

app.UseRequestLocalization(localizationOptions);

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapGet("/culture/set", (HttpContext context, string culture, string? redirectUri) =>
{
    if (!supportedCultures.Contains(culture, StringComparer.OrdinalIgnoreCase))
    {
        return Results.BadRequest();
    }

    context.Response.Cookies.Append(
        CookieRequestCultureProvider.DefaultCookieName,
        CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture)));

    return Results.LocalRedirect(string.IsNullOrWhiteSpace(redirectUri) ? "/" : redirectUri);
});

app.MapRazorPages();
app.MapRazorComponents<AdminPanel.App>()
    .AddInteractiveServerRenderMode();

app.Run();
