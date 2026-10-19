using System.Threading.RateLimiting;
using Application.Catalog;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace WebApi.Telegram;

public static class TelegramRegistration
{
    public static IServiceCollection AddTrebaTelegram(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<TelegramOptions>(configuration.GetSection("Telegram"));
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<TelegramInitDataValidator>(); services.AddSingleton<TelegramBotClient>();
        services.AddSingleton<ITelegramBotClient>(sp => sp.GetRequiredService<TelegramBotClient>());
        services.AddSingleton<IMarketplaceSourceClient>(_ => new MarketplaceSourceClient()); services.AddSingleton<MarketplaceProductParser>();
        services.AddScoped<TelegramLinkService>(); services.AddScoped<TelegramGuardService>(); services.AddScoped<ProductImportService>();
        services.AddScoped<SellerCategoryAccess>(); services.AddHostedService<TelegramBotWorker>();
        services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, TelegramAuthenticationHandler>(TelegramAuthenticationHandler.SchemeName, _ => { });
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            AddLimit(options, "telegram-sensitive", 12); AddLimit(options, "telegram-poll", 300); AddLimit(options, "telegram-import", 5);
        });
        return services;
    }
    private static void AddLimit(RateLimiterOptions options, string name, int count) => options.AddPolicy(name, context =>
        RateLimitPartition.GetFixedWindowLimiter((context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown") + ":" + name,
            _ => new FixedWindowRateLimiterOptions { PermitLimit = count, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true }));
}
