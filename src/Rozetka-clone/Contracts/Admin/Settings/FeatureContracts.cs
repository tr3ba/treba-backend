namespace Contracts.Admin.Settings;

public sealed record FeatureFlagDto(string Key, bool Enabled, Guid Version, DateTimeOffset? UpdatedAt);
public sealed record SetFeatureFlagRequest(bool Enabled, Guid ExpectedVersion);
