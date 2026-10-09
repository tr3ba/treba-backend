using Domain.Entities.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure.Persistence;

public static class AdministratorSeeder
{
    public static async Task SeedAdministratorAsync(
        this IServiceProvider services,
        IConfiguration config
    )
    {
        var email = config["BootstrapAdmin:Email"]?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(email))
        {
            return;
        }

        var password = config["BootstrapAdmin:Password"];
        if (string.IsNullOrEmpty(password)
            || password.Length < 12)
        {
            throw new InvalidOperationException("BootstrapAdmin:Password must be at least 12 characters.");
        }

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var existing = await db.Users
            .Include(u => u.Role)
            .SingleOrDefaultAsync(u => u.Email == email);
        if (existing is not null)
        {
            if (existing.Role?.Name != Roles.Administrator)
            {
                throw new InvalidOperationException(
                    "Bootstrap email belongs to a non-administrator. Choose a different email."
                );
            }

            return;
        }

        var administratorAlreadyExists = await db
            .Users
            .Include(u => u.Role)
            .AnyAsync(
                u =>
                    !u.IsDeleted
                    && u.Role != null
                    && u.Role.Name == Roles.Administrator
            );
        if (administratorAlreadyExists)
        {
            return;
        }

        var role = await db.Roles.SingleAsync(r => r.Name == Roles.Administrator);
        var user = User.Create(
            Guid.NewGuid(),
            email,
            null,
            "Treba",
            "Administrator"
        );
        user.AssignRole(role.Id);
        user.SetPasswordHash(BCrypt.Net.BCrypt.HashPassword(password));
        user.VerifyEmail();
        db.Users.Add(user);
        await db.SaveChangesAsync();
    }
}
