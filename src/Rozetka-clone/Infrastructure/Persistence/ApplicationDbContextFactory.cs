using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Infrastructure.Persistence;

public sealed class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        // Scaffolding a migration does not connect. Database commands must supply the real connection explicitly.
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? "Host=127.0.0.1;Database=treba_design;Username=treba_design;Password=design-only";
        return new(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connection).Options);
    }
}
