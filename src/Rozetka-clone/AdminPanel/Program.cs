using AdminPanel.Auth;
using AdminPanel.Services.Abstractions;
using AdminPanel.Services.Implementations;
using AdminPanel.State;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.DataProtection;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddLocalization();
builder.Services.AddAuthentication(WebsiteLogin.Scheme).AddCookie(WebsiteLogin.Scheme, options =>
{
    options.Cookie.Name = "treba_admin_session";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    options.SlidingExpiration = false;
});
builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient("website-login", client => client.BaseAddress = new Uri(builder.Configuration["ApiBaseUrl"] ?? "https://localhost:7107/"));
if (builder.Configuration["DataProtectionKeysPath"] is {} keys)
    builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(keys)).SetApplicationName("Treba.AdminPanel");
builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<CatalogApiClient>();
builder.Services.AddScoped<SellerImportsApiClient>();
builder.Services.AddScoped<SecurityApiClient>();
builder.Services.AddScoped<ReviewsApiClient>();
builder.Services.AddScoped<DashboardApiClient>();
builder.Services.AddScoped<ReviewModerationState>();
builder.Services.AddScoped<SettingsApiClient>();
builder.Services.AddScoped<TelegramApiClient>();
builder.Services.AddSingleton<UiTime>();

builder.Services.AddScoped<TokenStorageService>();
builder.Services.AddScoped<AuthorizedHttpMessageHandler>();

builder.Services.AddScoped<AuthenticationStateProvider, JwtAuthenticationStateProvider>();
builder.Services.AddScoped<JwtAuthenticationStateProvider>(sp =>
    (JwtAuthenticationStateProvider)sp.GetRequiredService<AuthenticationStateProvider>());

builder.Services.AddScoped(sp =>
{
    var handler = sp.GetRequiredService<AuthorizedHttpMessageHandler>();
    handler.InnerHandler ??= new HttpClientHandler();

    var client = new HttpClient(handler, disposeHandler: false)
    {
        BaseAddress = new Uri(builder.Configuration["ApiBaseUrl"] ?? "https://localhost:7107/")
    };
    return client;
});

builder.Services.AddScoped<IUsersApiClient, UsersApiClient>();
builder.Services.AddScoped<ISellersApiClient, SellersApiClient>();
builder.Services.AddScoped<OperationsApiClient>();
builder.Services.AddScoped<IProductsApiClient, ProductsApiClient>();
builder.Services.AddScoped<ICategoriesApiClient, CategoriesApiClient>();

builder.Services.AddScoped<UsersState>();
builder.Services.AddScoped<SellersState>();
builder.Services.AddScoped<ProductsState>();
builder.Services.AddScoped<CategoriesState>();
builder.Services.AddScoped<CurrentUserState>();
builder.Services.AddScoped<CatalogModerationState>();

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

if (!app.Environment.IsDevelopment() && !app.Configuration.GetValue<bool>("WebsiteLogin:Enabled"))
{
    app.UseHttpsRedirection();
}
app.UseStaticFiles();
app.MapStaticAssets();

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

Treba.Development.LocalLauncherEndpoints.Map(app);
WebsiteLogin.Map(app);
app.Run();
