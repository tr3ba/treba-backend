using AdminPanel.Services.Implementations;
using Microsoft.Extensions.Configuration;

namespace WebApi.Tests;

public sealed class UiTimeTests
{
    [Fact]
    public void AuditDayFilter_UsesVilniusMidnightInBothSeasons()
    {
        var time = new UiTime(new ConfigurationBuilder().Build());
        Assert.Equal(new DateTime(2026, 10, 16, 21, 0, 0, DateTimeKind.Utc), time.UtcStart(new DateOnly(2026, 10, 17)));
        Assert.Equal(new DateTime(2026, 12, 16, 22, 0, 0, DateTimeKind.Utc), time.UtcStart(new DateOnly(2026, 12, 17)));
    }
}
