using Domain.Entities.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure.Persistence;

public static class RoleSeeder
{
    private static readonly (string Name, string Description)[] DefaultRoles =
    [
        (Roles.Customer, "Покупець маркетплейсу"),
        (Roles.Seller, "Продавець маркетплейсу"),
        (Roles.Manager, "Менеджер магазину"),
        (Roles.Moderator, "Модератор контенту"),
        (Roles.Administrator, "Адміністратор платформи"),
    ];

    public static async Task SeedRolesAsync(
        this IServiceProvider serviceProvider
    )
    {
        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var existingRoles = await context.Roles
            .Select(r => r.Name)
            .ToListAsync();

        var missingRoles = DefaultRoles
            .Where(
                r =>
                    !existingRoles.Contains(
                        r.Name,
                        StringComparer.OrdinalIgnoreCase
                    )
            )
            .Select(
                r =>
                    Role.Create(
                        r.Name,
                        r.Description
                    )
            )
            .ToList();

        if (missingRoles.Count > 0)
        {
            await context.Roles.AddRangeAsync(missingRoles);
            await context.SaveChangesAsync();
        }
    }
}
