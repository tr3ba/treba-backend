using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Infrastructure.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261023110000_NormalizeProfileLanguages")]
public sealed class NormalizeProfileLanguages : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
        => migrationBuilder.Sql("""
            UPDATE user_profiles SET "Language" = CASE
                WHEN lower(trim("Language")) IN ('en', 'en-us', 'en-gb') THEN 'en'
                ELSE 'uk' END
            WHERE "Language" NOT IN ('uk', 'en');
            """);

    protected override void Down(MigrationBuilder migrationBuilder) { }
}
