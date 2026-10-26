using System.Threading.RateLimiting;
using Application.Abstractions;
using Application.Delivery;
using Application.Marketplace;
using Infrastructure.Authentication;
using WebApi.Notifications;

namespace WebApi.Delivery;

public static class DeliveryNotificationsRegistration
{
    public static IServiceCollection AddDeliveryNotifications(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<NovaPoshtaOptions>().Configure(o =>
            {
                configuration.GetSection("NovaPoshta").Bind(o);
                var aliasKey = o.ApiKey;
                configuration.GetSection("Delivery:NovaPoshta").Bind(o);
                if (string.IsNullOrWhiteSpace(o.ApiKey)) o.ApiKey = aliasKey;
                o.ApiKey = o.ApiKey.Trim();
            })
            .Validate(o => o.DefaultWeightKg is > 0 and <= 1000, "Default parcel weight must be between 0 and 1000 kg.").ValidateOnStart();
        services.AddMemoryCache(options => options.SizeLimit = 1024);
        services.AddHttpClient<IDeliveryProvider, NovaPoshtaClient>(client => client.Timeout = TimeSpan.FromSeconds(15))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        services.AddScoped<DeliveryService>();
        services.AddOptions<CustomerNotificationOptions>().Bind(configuration.GetSection("Notifications"))
            .Validate(o => !o.Enabled || Uri.TryCreate(o.StoreOrigin, UriKind.Absolute, out var uri) && uri.Scheme == "https"
                && uri.IsDefaultPort && string.IsNullOrEmpty(uri.UserInfo) && uri.AbsolutePath == "/"
                && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment), "Notifications:StoreOrigin must be an HTTPS origin.").ValidateOnStart();
        services.AddSingleton<INotificationChannels, NotificationChannels>();
        services.AddScoped<ITransactionalEmailSender, TransactionalEmailSender>();
        services.AddScoped<CustomerNotificationSettingsService>(); services.AddScoped<CustomerNotificationQueue>();
        services.AddScoped<CustomerNotificationRunner>(); services.AddHostedService<CustomerNotificationWorker>();
        services.AddRateLimiter(options => options.AddPolicy("delivery", context =>
            RateLimitPartition.GetFixedWindowLimiter(context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 60, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })));
        return services;
    }
}
