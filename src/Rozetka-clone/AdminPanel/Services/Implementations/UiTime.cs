namespace AdminPanel.Services.Implementations;

public sealed class UiTime(IConfiguration configuration)
{
    private readonly TimeZoneInfo _zone = TimeZoneInfo.FindSystemTimeZoneById(configuration["DisplayTimeZone"] ?? "Europe/Vilnius");
    public DateTimeOffset Local(DateTimeOffset value) => TimeZoneInfo.ConvertTime(value, _zone);
    public DateTime UtcStart(DateOnly value) => TimeZoneInfo.ConvertTimeToUtc(value.ToDateTime(TimeOnly.MinValue), _zone);
    public DateTimeOffset Utc(DateTime value) => new(TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(value, DateTimeKind.Unspecified), _zone));
}
