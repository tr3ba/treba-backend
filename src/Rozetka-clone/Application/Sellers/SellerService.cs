using System;
using System.Collections.Generic;
using System.Text;
using Application.Abstractions;
using Domain.Entities;
using Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Application.Sellers
{
    public sealed class SellerService : ISellerService
    {
        private readonly IApplicationDbContext _dbContext;

        public SellerService(
            IApplicationDbContext dbContext
        )
        {
            _dbContext = dbContext;
        }

        public async Task<SellerDto> CreateAsync(
            CreateSellerRequest request,
            CancellationToken cancellationToken = default
        )
        {
            var userExists = await _dbContext.Users.AnyAsync(
                x => x.Id == request.UserId,
                cancellationToken
            );

            if (!userExists)
            {
                throw new InvalidOperationException("User not found.");
            }

            var sellerExists = await _dbContext.Sellers.AnyAsync(
                x => x.UserId == request.UserId,
                cancellationToken
            );

            if (sellerExists)
            {
                throw new InvalidOperationException("Seller profile already exists for this user.");
            }

            var companyName = NormalizeRequired(
                request.CompanyName,
                "Company name"
            );

            var taxNumber = NormalizeRequired(
                request.TaxNumber,
                "Tax number"
            );

            var taxNumberExists = await _dbContext.Sellers.AnyAsync(
                x => x.TaxNumber == taxNumber,
                cancellationToken
            );

            if (taxNumberExists)
            {
                throw new InvalidOperationException("Seller with this tax number already exists.");
            }

            var now = DateTime.UtcNow;

            var seller = new Seller
            {
                Id = Guid.NewGuid(),
                UserId = request.UserId,
                CompanyName = companyName,
                TaxNumber = taxNumber,
                Description = NormalizeOptional(request.Description),
                Phone = NormalizeOptional(request.Phone),
                Email = NormalizeOptional(request.Email),
                Status = SellerStatus.PENDING,
                CreatedAt = now,
                UpdatedAt = now,
            };

            _dbContext.Sellers.Add(seller);

            await _dbContext.SaveChangesAsync(cancellationToken);

            return ToDto(seller);
        }

        public async Task<SellerDto?> GetByIdAsync(
            Guid sellerId,
            CancellationToken cancellationToken = default
        )
        {
            return await _dbContext
                .Sellers
                .AsNoTracking()
                .Where(x => x.Id == sellerId)
                .Select(x => ToDtoProjection(x))
                .FirstOrDefaultAsync(cancellationToken);
        }

        public async Task<SellerDto?> GetByUserIdAsync(
            Guid userId,
            CancellationToken cancellationToken = default
        )
        {
            return await _dbContext
                .Sellers
                .AsNoTracking()
                .Where(x => x.UserId == userId)
                .Select(x => ToDtoProjection(x))
                .FirstOrDefaultAsync(cancellationToken);
        }

        public async Task<IReadOnlyList<SellerDto>> GetAllAsync(
            CancellationToken cancellationToken = default
        )
        {
            return await _dbContext
                .Sellers
                .AsNoTracking()
                .OrderByDescending(x => x.CreatedAt)
                .Select(x => ToDtoProjection(x))
                .ToListAsync(cancellationToken);
        }

        public async Task<SellerDto?> UpdateAsync(
            Guid sellerId,
            UpdateSellerRequest request,
            CancellationToken cancellationToken = default
        )
        {
            var seller = await _dbContext.Sellers.FirstOrDefaultAsync(
                x => x.Id == sellerId,
                cancellationToken
            );

            if (seller is null)
            {
                return null;
            }

            if (request.CompanyName is not null)
            {
                seller.CompanyName = NormalizeRequired(
                    request.CompanyName,
                    "Company name"
                );
            }

            if (request.TaxNumber is not null)
            {
                var taxNumber = NormalizeRequired(
                    request.TaxNumber,
                    "Tax number"
                );

                var exists = await _dbContext.Sellers.AnyAsync(
                    x =>
                        x.Id != sellerId
                        && x.TaxNumber == taxNumber,
                    cancellationToken
                );

                if (exists)
                {
                    throw new InvalidOperationException("Seller with this tax number already exists.");
                }

                seller.TaxNumber = taxNumber;
            }

            if (request.Description is not null)
            {
                seller.Description = NormalizeOptional(request.Description);
            }

            if (request.Phone is not null)
            {
                seller.Phone = NormalizeOptional(request.Phone);
            }

            if (request.Email is not null)
            {
                seller.Email = NormalizeOptional(request.Email);
            }

            seller.UpdatedAt = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync(cancellationToken);

            return ToDto(seller);
        }

        public async Task<SellerDto?> ApproveAsync(
            Guid sellerId,
            CancellationToken cancellationToken = default
        )
        {
            var seller = await _dbContext.Sellers.FirstOrDefaultAsync(
                x => x.Id == sellerId,
                cancellationToken
            );

            if (seller is null)
            {
                return null;
            }

            seller.Status = SellerStatus.ACTIVE;
            seller.UpdatedAt = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync(cancellationToken);

            return ToDto(seller);
        }

        public async Task<SellerDto?> SuspendAsync(
            Guid sellerId,
            CancellationToken cancellationToken = default
        )
        {
            var seller = await _dbContext.Sellers.FirstOrDefaultAsync(
                x => x.Id == sellerId,
                cancellationToken
            );

            if (seller is null)
            {
                return null;
            }

            seller.Status = SellerStatus.SUSPENDED;
            seller.UpdatedAt = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync(cancellationToken);

            return ToDto(seller);
        }

        private static SellerDto ToDto(
            Seller seller
        )
        {
            return new SellerDto
            {
                Id = seller.Id,
                UserId = seller.UserId,
                CompanyName = seller.CompanyName,
                TaxNumber = seller.TaxNumber,
                Description = seller.Description,
                Phone = seller.Phone,
                Email = seller.Email,
                Status = seller.Status,
                CreatedAt = seller.CreatedAt,
                UpdatedAt = seller.UpdatedAt,
            };
        }

        private static SellerDto ToDtoProjection(
            Seller seller
        )
        {
            return new SellerDto
            {
                Id = seller.Id,
                UserId = seller.UserId,
                CompanyName = seller.CompanyName,
                TaxNumber = seller.TaxNumber,
                Description = seller.Description,
                Phone = seller.Phone,
                Email = seller.Email,
                Status = seller.Status,
                CreatedAt = seller.CreatedAt,
                UpdatedAt = seller.UpdatedAt,
            };
        }

        private static string NormalizeRequired(
            string value,
            string fieldName
        )
        {
            var normalized = value.Trim();

            if (string.IsNullOrWhiteSpace(normalized))
            {
                throw new ArgumentException($"{fieldName} is required.");
            }

            return normalized;
        }

        private static string? NormalizeOptional(
            string? value
        )
        {
            return string.IsNullOrWhiteSpace(value)
                ? null
                : value.Trim();
        }
    }
}
