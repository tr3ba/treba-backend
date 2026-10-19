namespace Contracts.Admin.Dashboard;

public sealed record AdminDashboardDto(
    int Days,
    int? PendingSellers,
    int? ActiveSellers,
    int? SuspendedSellers,
    int PendingReviews,
    int PublishedReviews,
    int RejectedReviews,
    int? TotalUsers,
    int? NewUsers,
    IReadOnlyList<AdminActivityDto> RecentActivity);

public sealed record AdminActivityDto(
    string Kind,
    string Status,
    Guid EntityId,
    string Name,
    DateTimeOffset OccurredAt);
