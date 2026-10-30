using Application.Abstractions;
using Contracts.Admin.Dashboard;
using Contracts.Admin.Reviews;
using Contracts.Common;
using Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Application.Administration;

public sealed class AdminWorkspaceService(IApplicationDbContext db)
{
    public async Task<PagedResponse<AdminReviewDto>> ReviewsAsync(
        int page, int size, string? status, string? search, CancellationToken ct = default)
    {
        if (page < 1 || size is < 1 or > 100 || ((long)page - 1) * size > int.MaxValue)
            throw new ArgumentException("Page must be positive and size must be between 1 and 100.");

        var reviews = db.Reviews.AsNoTracking().Where(review => review.Status != ReviewStatus.DELETED);
        if (!string.IsNullOrWhiteSpace(status))
        {
            var parsed = ParseReviewStatus(status);
            reviews = reviews.Where(review => review.Status == parsed);
        }

        var query = from review in reviews
                    join product in db.Products.AsNoTracking() on review.ProductId equals product.Id
                    join user in db.Users.AsNoTracking() on review.UserId equals user.Id
                    select new { Review = review, Product = product, User = user };

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(row => row.Product.Name.ToLower().Contains(term)
                || row.Review.Title.ToLower().Contains(term)
                || row.Review.Comment.ToLower().Contains(term)
                || row.User.Email.ToLower().Contains(term)
                || (row.User.FirstName + " " + row.User.LastName).ToLower().Contains(term));
        }

        var total = await query.LongCountAsync(ct);
        var rows = await query.OrderByDescending(row => row.Review.CreatedAt)
            .ThenBy(row => row.Review.Id).Skip((page - 1) * size).Take(size)
            .Select(row => new
            {
                row.Review,
                ProductName = row.Product.Name,
                ProductSlug = row.Product.Slug,
                row.User.FirstName,
                row.User.LastName,
                AuthorEmail = row.User.Email
            }).ToListAsync(ct);
        var content = rows.Select(row => new AdminReviewDto(
            row.Review.Id, row.Review.ProductId, row.ProductName, row.ProductSlug,
            row.Review.UserId, $"{row.FirstName} {row.LastName}".Trim(), row.AuthorEmail,
            row.Review.Rating, row.Review.Title, row.Review.Comment, row.Review.Status.ToString(),
            row.Review.VerifiedPurchase, row.Review.CreatedAt, row.Review.UpdatedAt,
            row.Review.StaffReply, row.Review.StaffRepliedAt)).ToList();

        return new(content, page, size, total, (int)Math.Ceiling(total / (double)size));
    }

    public Task<int> PendingReviewsAsync(CancellationToken ct = default) =>
        db.Reviews.CountAsync(review => review.Status == ReviewStatus.PENDING, ct);

    public static ReviewStatus ParseReviewStatus(string status) => status.Trim().ToUpperInvariant() switch
    {
        "PENDING" => ReviewStatus.PENDING,
        "PUBLISHED" => ReviewStatus.PUBLISHED,
        "REJECTED" => ReviewStatus.REJECTED,
        _ => throw new ArgumentException("Unknown review status.")
    };

    public async Task<AdminDashboardDto> DashboardAsync(
        int days, bool includeSellers, bool includeUsers, CancellationToken ct = default)
    {
        if (days is not (7 or 30 or 90))
            throw new ArgumentException("Period must be 7, 30 or 90 days.");

        var since = DateTimeOffset.UtcNow.AddDays(-days);
        var sinceUtc = since.UtcDateTime;
        var reviewCounts = await db.Reviews.AsNoTracking().GroupBy(review => review.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() }).ToDictionaryAsync(x => x.Status, x => x.Count, ct);

        int? pendingSellers = null, activeSellers = null, suspendedSellers = null, totalUsers = null, newUsers = null;
        var activity = new List<AdminActivityDto>();

        if (includeSellers)
        {
            var sellerCounts = await db.Sellers.AsNoTracking().GroupBy(seller => seller.Status)
                .Select(group => new { Status = group.Key, Count = group.Count() }).ToDictionaryAsync(x => x.Status, x => x.Count, ct);
            pendingSellers = sellerCounts.GetValueOrDefault(SellerStatus.PENDING);
            activeSellers = sellerCounts.GetValueOrDefault(SellerStatus.ACTIVE);
            suspendedSellers = sellerCounts.GetValueOrDefault(SellerStatus.SUSPENDED);
            var sellers = await db.Sellers.AsNoTracking().Where(seller => seller.UpdatedAt >= sinceUtc)
                .OrderByDescending(seller => seller.UpdatedAt).ThenBy(seller => seller.Id).Take(8).ToListAsync(ct);
            activity.AddRange(sellers.Select(seller => new AdminActivityDto(
                "seller", seller.Status.ToString(), seller.Id, seller.CompanyName, Utc(seller.UpdatedAt))));
        }

        if (includeUsers)
        {
            totalUsers = await db.Users.CountAsync(user => !user.IsDeleted, ct);
            newUsers = await db.Users.CountAsync(user => !user.IsDeleted && user.CreatedAt >= since, ct);
            var users = await db.Users.AsNoTracking().Where(user => !user.IsDeleted && user.CreatedAt >= since)
                .OrderByDescending(user => user.CreatedAt).ThenBy(user => user.Id).Take(8).ToListAsync(ct);
            activity.AddRange(users.Select(user => new AdminActivityDto(
                "user", "CREATED", user.Id, $"{user.FirstName} {user.LastName}".Trim(), user.CreatedAt)));
        }

        var reviews = await (from review in db.Reviews.AsNoTracking()
                             join product in db.Products.AsNoTracking() on review.ProductId equals product.Id
                             where review.Status != ReviewStatus.DELETED && review.UpdatedAt >= sinceUtc
                             orderby review.UpdatedAt descending, review.Id
                             select new { Review = review, ProductName = product.Name }).Take(8).ToListAsync(ct);
        activity.AddRange(reviews.Select(row => new AdminActivityDto(
            "review", row.Review.Status.ToString(), row.Review.Id, row.ProductName, Utc(row.Review.UpdatedAt))));

        var products = await db.Products.AsNoTracking().Where(product => product.UpdatedAt >= sinceUtc)
            .OrderByDescending(product => product.UpdatedAt).ThenBy(product => product.Id).Take(8).ToListAsync(ct);
        activity.AddRange(products.Select(product => new AdminActivityDto(
            "product", product.Status.ToString(), product.Id, product.Name, Utc(product.UpdatedAt))));

        return new(days, pendingSellers, activeSellers, suspendedSellers,
            reviewCounts.GetValueOrDefault(ReviewStatus.PENDING), reviewCounts.GetValueOrDefault(ReviewStatus.PUBLISHED),
            reviewCounts.GetValueOrDefault(ReviewStatus.REJECTED), totalUsers, newUsers,
            activity.OrderByDescending(item => item.OccurredAt).ThenBy(item => item.EntityId).Take(8).ToList());
    }

    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
