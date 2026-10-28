using Application.Abstractions;
using Application.Common;
using Domain.Enums;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Reviews
{
    public sealed class ReviewService : IReviewService
    {
        private readonly IApplicationDbContext _dbContext;

        public ReviewService(IApplicationDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task<PagedResult<ReviewableProductDto>> GetProductsAwaitingReviewAsync(Guid userId, int page, CancellationToken cancellationToken = default)
        {
            if (page is < 1 or > 100000) throw new ArgumentOutOfRangeException(nameof(page));
            var query = from item in _dbContext.OrderItems.AsNoTracking()
                join order in _dbContext.Orders.AsNoTracking() on item.OrderId equals order.Id
                join product in _dbContext.Products.AsNoTracking() on item.ProductId equals product.Id
                where order.CustomerId == userId && order.PaymentStatus == PaymentStatus.SUCCEEDED
                    && (order.Status == OrderStatus.DELIVERED || order.Status == OrderStatus.COMPLETED)
                    && !_dbContext.Reviews.Any(review => review.OrderItemId == item.Id)
                orderby order.CreatedAt descending, item.Id
                select new ReviewableProductDto(item.Id, item.ProductId, item.ProductName, product.Slug,
                    _dbContext.ProductImages.Where(image => image.ProductId == item.ProductId)
                        .OrderByDescending(image => image.IsMain).ThenBy(image => image.SortOrder).ThenBy(image => image.Id)
                        .Select(image => image.ImageUrl).FirstOrDefault(), order.Id, order.OrderNumber);
            var count = await query.CountAsync(cancellationToken);
            return new() { Items = await query.Skip((page - 1) * 20).Take(20).ToListAsync(cancellationToken),
                Page = page, PageSize = 20, TotalCount = count };
        }

        public async Task<PagedResult<ReviewDto>> GetMyReviewsAsync(Guid userId, int page, CancellationToken cancellationToken = default)
        {
            if (page is < 1 or > 100000) throw new ArgumentOutOfRangeException(nameof(page));
            var query = _dbContext.Reviews.AsNoTracking().Where(review => review.UserId == userId && review.Status != ReviewStatus.DELETED);
            var count = await query.CountAsync(cancellationToken);
            var items = await query.OrderByDescending(review => review.CreatedAt).ThenBy(review => review.Id)
                .Skip((page - 1) * 20).Take(20).Select(review => new ReviewDto
                {
                    Id = review.Id, ProductId = review.ProductId, UserId = review.UserId, OrderItemId = review.OrderItemId,
                    Rating = review.Rating, Title = review.Title, Comment = review.Comment, Status = review.Status,
                    VerifiedPurchase = review.VerifiedPurchase, HelpfulCount = review.HelpfulCount,
                    CreatedAt = review.CreatedAt, UpdatedAt = review.UpdatedAt
                }).ToListAsync(cancellationToken);
            return new() { Items = items, Page = page, PageSize = 20, TotalCount = count };
        }

        public async Task<ReviewDto> CreateReviewAsync(
            Guid userId,
            CreateReviewRequest request,
            CancellationToken cancellationToken = default)
        {
            if (request.Rating < 1 || request.Rating > 5)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(request.Rating),
                    "Rating must be between 1 and 5.");
            }

            if (request.Title is null || request.Title.Length > 255 || string.IsNullOrWhiteSpace(request.Comment) || request.Comment.Length > 10000)
                throw new ArgumentException("Вкажіть текст відгуку до 10000 символів і заголовок до 255 символів.");

            var orderItem = await _dbContext.OrderItems
                .AsNoTracking()
                .Where(x =>
                    x.Id == request.OrderItemId &&
                    x.ProductId == request.ProductId)
                .Select(x => new
                {
                    x.Id,
                    x.ProductId,
                    x.OrderId
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (orderItem is null)
            {
                throw new KeyNotFoundException("Order item not found.");
            }

            var order = await _dbContext.Orders
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    x =>
                        x.Id == orderItem.OrderId &&
                        x.CustomerId == userId,
                    cancellationToken);

            if (order is null)
            {
                throw new BusinessRuleException(
                    "Відгук може залишити лише покупець цього товару.");
            }

            if (!ReviewEligibility.IsPurchased(order.Status, order.PaymentStatus))
                throw new BusinessRuleException("Відгук можна залишити після оплати та отримання товару.");

            var reviewAlreadyExists = await _dbContext.Reviews
                .AsNoTracking()
                .AnyAsync(
                    x => x.OrderItemId == request.OrderItemId,
                    cancellationToken);

            if (reviewAlreadyExists)
            {
                throw new BusinessRuleException(
                    "Ви вже залишили відгук про цей товар із замовлення.");
            }

            var now = DateTime.UtcNow;

            var review = new Domain.Entities.Review
            {
                Id = Guid.NewGuid(),
                ProductId = orderItem.ProductId,
                UserId = userId,
                OrderItemId = orderItem.Id,
                Rating = request.Rating,
                Title = request.Title.Trim(),
                Comment = request.Comment.Trim(),
                Status = ReviewStatus.PENDING,
                VerifiedPurchase = true,
                HelpfulCount = 0,
                CreatedAt = now,
                UpdatedAt = now
            };

            _dbContext.Reviews.Add(review);

            await _dbContext.SaveChangesAsync(cancellationToken);

            return new ReviewDto
            {
                Id = review.Id,
                ProductId = review.ProductId,
                UserId = review.UserId,
                OrderItemId = review.OrderItemId,
                Rating = review.Rating,
                Title = review.Title,
                Comment = review.Comment,
                Status = review.Status,
                VerifiedPurchase = review.VerifiedPurchase,
                HelpfulCount = review.HelpfulCount,
                CreatedAt = review.CreatedAt,
                UpdatedAt = review.UpdatedAt
            };
        }

        public async Task<ReviewDto> UpdateReviewAsync(
            Guid reviewId,
            Guid userId,
            UpdateReviewRequest request,
            CancellationToken cancellationToken = default)
        {
            if (request.Rating < 1 || request.Rating > 5)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(request.Rating),
                    "Rating must be between 1 and 5.");
            }

            var review = await _dbContext.Reviews
                .FirstOrDefaultAsync(
                    x => x.Id == reviewId && x.UserId == userId,
                    cancellationToken);

            if (review is null)
            {
                throw new KeyNotFoundException("Review not found.");
            }

            review.Rating = request.Rating;
            review.Title = request.Title;
            review.Comment = request.Comment;
            review.Status = ReviewStatus.PENDING;
            review.UpdatedAt = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync(cancellationToken);

            await RecalculateProductRatingAsync(
                review.ProductId,
                cancellationToken);

            return new ReviewDto
            {
                Id = review.Id,
                ProductId = review.ProductId,
                UserId = review.UserId,
                OrderItemId = review.OrderItemId,
                Rating = review.Rating,
                Title = review.Title,
                Comment = review.Comment,
                Status = review.Status,
                VerifiedPurchase = review.VerifiedPurchase,
                HelpfulCount = review.HelpfulCount,
                CreatedAt = review.CreatedAt,
                UpdatedAt = review.UpdatedAt
            };
        }

        public async Task DeleteReviewAsync(
            Guid reviewId,
            Guid userId,
            CancellationToken cancellationToken = default)
        {
            var review = await _dbContext.Reviews
                .FirstOrDefaultAsync(
                    x => x.Id == reviewId && x.UserId == userId,
                    cancellationToken);

            if (review is null)
            {
                throw new KeyNotFoundException("Review not found.");
            }

            if (review.Status == ReviewStatus.DELETED)
            {
                return;
            }

            review.Status = ReviewStatus.DELETED;
            review.UpdatedAt = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync(cancellationToken);

            await RecalculateProductRatingAsync(
                review.ProductId,
                cancellationToken);
        }

        public async Task ModerateReviewAsync(
            Guid reviewId,
            ReviewStatus status,
            CancellationToken cancellationToken = default)
        {
            if (status is not (ReviewStatus.PUBLISHED or ReviewStatus.REJECTED))
                throw new ArgumentException("Choose PUBLISHED or REJECTED.", nameof(status));

            var review = await _dbContext.Reviews
                .FirstOrDefaultAsync(
                    x => x.Id == reviewId,
                    cancellationToken);

            if (review is null)
            {
                throw new KeyNotFoundException("Review not found.");
            }

            if (review.Status == ReviewStatus.DELETED)
                throw new BusinessRuleException("Deleted reviews cannot be moderated.");
            if (review.Status == status) return;

            var product = await _dbContext.Products.FirstOrDefaultAsync(x => x.Id == review.ProductId, cancellationToken)
                ?? throw new KeyNotFoundException("Product not found.");
            var otherRatings = await _dbContext.Reviews.AsNoTracking()
                .Where(x => x.ProductId == review.ProductId && x.Id != reviewId && x.Status == ReviewStatus.PUBLISHED)
                .GroupBy(x => x.ProductId)
                .Select(group => new { Count = group.Count(), Sum = group.Sum(x => x.Rating) })
                .SingleOrDefaultAsync(cancellationToken);
            var count = (otherRatings?.Count ?? 0) + (status == ReviewStatus.PUBLISHED ? 1 : 0);
            var sum = (otherRatings?.Sum ?? 0) + (status == ReviewStatus.PUBLISHED ? review.Rating : 0);

            review.Status = status;
            review.UpdatedAt = DateTime.UtcNow;
            product.ReviewCount = count;
            product.AverageRating = count == 0 ? 0 : sum / (double)count;
            product.UpdatedAt = review.UpdatedAt;

            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        public async Task VoteReviewAsync(
            Guid reviewId,
            Guid userId,
            bool helpful,
            CancellationToken cancellationToken = default)
        {
            var review = await _dbContext.Reviews
                .FirstOrDefaultAsync(
                    x =>
                        x.Id == reviewId
                        && x.Status == ReviewStatus.PUBLISHED,
                    cancellationToken);

            if (review is null)
            {
                throw new KeyNotFoundException("Review not found.");
            }

            var vote = await _dbContext.ReviewVotes
                .FirstOrDefaultAsync(
                    x =>
                        x.ReviewId == reviewId
                        && x.UserId == userId,
                    cancellationToken);

            if (vote is null)
            {
                vote = new Domain.Entities.ReviewVote
                {
                    Id = Guid.NewGuid(),
                    ReviewId = reviewId,
                    UserId = userId,
                    Helpful = helpful
                };

                _dbContext.ReviewVotes.Add(vote);
            }
            else
            {
                vote.Helpful = helpful;
            }

            await _dbContext.SaveChangesAsync(cancellationToken);

            review.HelpfulCount = await _dbContext.ReviewVotes
                .AsNoTracking()
                .CountAsync(
                    x =>
                        x.ReviewId == reviewId
                        && x.Helpful,
                    cancellationToken);

            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        public async Task<PagedResult<ReviewDto>> GetProductReviewsAsync(
            Guid productId,
            int page,
            int pageSize,
            CancellationToken cancellationToken = default)
        {
            if (page < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(page),
                    "Page must be greater than zero.");
            }

            if (pageSize < 1 || pageSize > 100)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(pageSize),
                    "Page size must be between 1 and 100.");
            }

            var query = _dbContext.Reviews
                .AsNoTracking()
                .Where(x =>
                    x.ProductId == productId
                    && x.Status == ReviewStatus.PUBLISHED);

            var totalCount = await query.CountAsync(cancellationToken);

            var items = await query
                .OrderByDescending(x => x.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(x => new ReviewDto
                {
                    Id = x.Id,
                    ProductId = x.ProductId,
                    UserId = x.UserId,
                    OrderItemId = x.OrderItemId,
                    Rating = x.Rating,
                    Title = x.Title,
                    Comment = x.Comment,
                    Status = x.Status,
                    VerifiedPurchase = x.VerifiedPurchase,
                    HelpfulCount = x.HelpfulCount,
                    CreatedAt = x.CreatedAt,
                    UpdatedAt = x.UpdatedAt
                })
                .ToListAsync(cancellationToken);

            return new PagedResult<ReviewDto>
            {
                Items = items,
                Page = page,
                PageSize = pageSize,
                TotalCount = totalCount
            };
        }

        public async Task RecalculateProductRatingAsync(
            Guid productId,
            CancellationToken cancellationToken = default)
        {
            var product = await _dbContext.Products
                .FirstOrDefaultAsync(
                    x => x.Id == productId,
                    cancellationToken);

            if (product is null)
            {
                throw new KeyNotFoundException("Product not found.");
            }

            var ratings = await _dbContext.Reviews
                .AsNoTracking()
                .Where(x =>
                    x.ProductId == productId
                    && x.Status == ReviewStatus.PUBLISHED)
                .Select(x => x.Rating)
                .ToListAsync(cancellationToken);

            product.ReviewCount = ratings.Count;

            product.AverageRating = ratings.Count == 0
                ? 0
                : ratings.Average();

            product.UpdatedAt = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
