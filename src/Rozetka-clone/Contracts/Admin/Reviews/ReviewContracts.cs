using System.ComponentModel.DataAnnotations;

namespace Contracts.Admin.Reviews;

public sealed record AdminReviewDto(
    Guid Id,
    Guid ProductId,
    string ProductName,
    string ProductSlug,
    Guid UserId,
    string AuthorName,
    string AuthorEmail,
    int Rating,
    string Title,
    string Comment,
    string Status,
    bool VerifiedPurchase,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record ModerateReviewRequest([Required] string Status);
