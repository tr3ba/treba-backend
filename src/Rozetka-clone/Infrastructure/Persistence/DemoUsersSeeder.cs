using Domain.Entities.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure.Persistence;

public static class DemoUsersSeeder
{
    private static readonly DemoUser[] DemoUsers =
    [
        new(
            Guid.Parse("a92c6542-dde4-44c8-97e5-8c61ce600001"),
            "anna.demo@treba.local",
            "+380501110001",
            "Анна",
            "Коваль"),
        new(
            Guid.Parse("a92c6542-dde4-44c8-97e5-8c61ce600002"),
            "bohdan.demo@treba.local",
            "+380501110002",
            "Богдан",
            "Мельник"),
        new(
            Guid.Parse("a92c6542-dde4-44c8-97e5-8c61ce600003"),
            "iryna.demo@treba.local",
            "+380501110003",
            "Ірина",
            "Шевченко"),
        new(
            Guid.Parse("a92c6542-dde4-44c8-97e5-8c61ce600004"),
            "maksym.demo@treba.local",
            "+380501110004",
            "Максим",
            "Бондар"),
        new(
            Guid.Parse("a92c6542-dde4-44c8-97e5-8c61ce600005"),
            "olena.demo@treba.local",
            "+380501110005",
            "Олена",
            "Ткаченко")
    ];

    public static async Task<int> SeedDemoUsersAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var demoEmails = DemoUsers.Select(user => user.Email).ToArray();

        var existingEmails = await dbContext.Users
            .AsNoTracking()
            .Where(user => demoEmails.Contains(user.Email))
            .Select(user => user.Email)
            .ToListAsync(cancellationToken);

        var existingEmailSet = existingEmails.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var demoUser in DemoUsers.Where(user => !existingEmailSet.Contains(user.Email)))
        {
            var user = User.Create(
                demoUser.Id,
                demoUser.Email,
                demoUser.Phone,
                demoUser.FirstName,
                demoUser.LastName);

            user.VerifyEmail();
            dbContext.Users.Add(user);
        }

        return await dbContext.SaveChangesAsync(cancellationToken);
    }

    private sealed record DemoUser(
        Guid Id,
        string Email,
        string Phone,
        string FirstName,
        string LastName);
}
