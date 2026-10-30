using Application.Common;
using Domain.Enums;
using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Reviews
{
    public interface IReviewService
    {
        Task<PagedResult<ReviewableProductDto>> GetProductsAwaitingReviewAsync(Guid userId, int page, CancellationToken cancellationToken = default);
        Task<PagedResult<ReviewDto>> GetMyReviewsAsync(Guid userId, int page, CancellationToken cancellationToken = default);

        Task<ReviewDto> CreateReviewAsync(
            Guid userId,
            CreateReviewRequest request,
            CancellationToken cancellationToken = default);

        Task<ReviewDto> UpdateReviewAsync(
            Guid reviewId,
            Guid userId,
            UpdateReviewRequest request,
            CancellationToken cancellationToken = default);

        Task DeleteReviewAsync(
            Guid reviewId,
            Guid userId,
            CancellationToken cancellationToken = default);

        Task ModerateReviewAsync(
            Guid reviewId,
            ReviewStatus status,
            CancellationToken cancellationToken = default,
            string? reply = null,
            Guid? repliedBy = null);

        Task VoteReviewAsync(
            Guid reviewId,
            Guid userId,
            bool helpful,
            CancellationToken cancellationToken = default);

        Task<PagedResult<ReviewDto>> GetProductReviewsAsync(
            Guid productId,
            int page,
            int pageSize,
            CancellationToken cancellationToken = default);

        Task RecalculateProductRatingAsync(
            Guid productId,
            CancellationToken cancellationToken = default);
    }
}
