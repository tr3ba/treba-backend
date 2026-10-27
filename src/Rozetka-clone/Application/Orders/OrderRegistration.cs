using Microsoft.Extensions.DependencyInjection;

namespace Application.Orders;

public static class OrderRegistration
{
    public static IServiceCollection AddOrderServices(this IServiceCollection services)
    {
        // Checkout and fulfillment record payment manually. Online payments use their separate endpoints.
        services.AddScoped<OrderService>();
        services.AddScoped<IOrderService>(sp => sp.GetRequiredService<OrderService>());
        return services;
    }
}
