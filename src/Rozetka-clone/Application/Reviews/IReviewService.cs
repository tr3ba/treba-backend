using Application.Common;
using Domain.Enums;
using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Reviews
{
    public interface IReviewService
    {
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
            CancellationToken cancellationToken = default);

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
