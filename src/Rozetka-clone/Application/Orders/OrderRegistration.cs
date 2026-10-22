using Application.Payments;
using Microsoft.Extensions.DependencyInjection;

namespace Application.Orders;

public static class OrderRegistration
{
    public static IServiceCollection AddOrderServices(this IServiceCollection services)
    {
        // Reading orders/support must not initialize an external payment provider.
        // Resolve payments only when checkout actually starts one, after the scoped order service exists.
        services.AddScoped<Func<IPaymentService>>(sp => () => sp.GetRequiredService<IPaymentService>());
        services.AddScoped<OrderService>();
        services.AddScoped<IOrderService>(sp => sp.GetRequiredService<OrderService>());
        return services;
    }
}
