using System.ComponentModel.DataAnnotations;
using Application.Abstractions;
using Application.Common;
using Contracts.Common;
using Contracts.Marketplace;
using Domain.Entities;
using Domain.Entities.Telegram;
using Domain.Entities.Users;
using Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Application.Marketplace;

public sealed class SellerApplicationService(IApplicationDbContext db, ICurrentActor actor)
{
    private Guid UserId => actor.UserId ?? throw new UnauthorizedAccessException();

    private void Staff()
    {
        if (actor.Role is not (Roles.Administrator or Roles.Manager))
            throw new UnauthorizedAccessException();
    }

    public async Task<SellerApplicationView?> MineAsync(CancellationToken ct) =>
        await db.SellerApplications.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == UserId, ct) is { } row ? View(row) : null;

    public async Task<SellerApplicationView> ApplyAsync(SellerApplicationInput input, CancellationToken ct)
    {
        var errors = new List<ValidationResult>();

        if (!Validator.TryValidateObject(input, new(input), errors, true))
            throw new ArgumentException(string.Join(" ", errors.Select(x => x.ErrorMessage)));

        var user = await db.Users.Include(x => x.Role).SingleAsync(x => x.Id == UserId, ct);
        var userIsNotAnActiveCustomer = user.Status != UserStatus.Active
            || user.Role?.Name != Roles.Customer
            || actor.Role != Roles.Customer;

        if (userIsNotAnActiveCustomer)
            throw new BusinessRuleException("Заявка продавця доступна лише для облікового запису покупця.");

        if (await db.Sellers.AnyAsync(x => x.UserId == user.Id, ct))
            throw new BusinessRuleException("Для цього акаунта вже створено продавця.");

        ValidateWebsite(input.Website);

        var row = await db.SellerApplications.SingleOrDefaultAsync(x => x.UserId == UserId, ct);

        if (row is not null && row.Status != "rejected")
            throw new BusinessRuleException("Заявку вже надіслано. Перевірте її статус.");

        if (row is null)
        {
            row = new()
            {
                UserId = UserId
            };
            db.SellerApplications.Add(row);
        }
        row.CompanyName = input.CompanyName.Trim();
        row.StoreName = input.StoreName.Trim();
        row.TaxNumber = input.TaxNumber.Trim();

        var hasShortCompanyName = row.CompanyName.Length < 2;
        var hasShortStoreName = row.StoreName.Length < 2;
        var hasShortTaxNumber = row.TaxNumber.Length < 2;

        if (hasShortCompanyName || hasShortStoreName || hasShortTaxNumber)
            throw new ArgumentException("Заповніть назву компанії, магазину та податковий номер.");

        row.Website = input.Website?.Trim();
        row.ContactName = input.ContactName.Trim();
        row.Position = input.Position.Trim();
        row.Email = input.Email.Trim();
        row.Phone = input.Phone.Trim();
        row.IsNonResident = input.IsNonResident;
        row.Status = "pending";
        row.ReviewNote = null;
        row.ReviewedBy = null;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        row.Version = Guid.NewGuid();
        await db.SaveChangesAsync(ct);
        return View(row);
    }

    public async Task<PagedResponse<SellerApplicationView>> ListAsync(int page, string? status, CancellationToken ct)
    {
        Staff();
        var hasInvalidPage = page is < 1 or > 100000;
        var hasUnknownStatus = status is not (null or "" or "pending" or "approved" or "rejected");

        if (hasInvalidPage || hasUnknownStatus)
            throw new ArgumentException("Invalid application filter.");

        var query = db.SellerApplications.AsNoTracking();

        if (!string.IsNullOrEmpty(status))
            query = query.Where(x => x.Status == status);
        var count = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).Skip((page - 1) * 20).Take(20).ToListAsync(ct);
        return new(rows.Select(View).ToList(), page, 20, count, (int)Math.Ceiling(count / 20d));
    }

    public async Task<SellerApplicationView> ReviewAsync(Guid id, ReviewSellerApplicationRequest input, CancellationToken ct)
    {
        Staff();
        var row = await db.SellerApplications.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException();
        var isNotPending = row.Status != "pending";
        var versionHasChanged = row.Version != input.Version;

        if (isNotPending || versionHasChanged)
            throw new BusinessRuleException("Заявку вже розглянуто або змінено. Оновіть список.");

        var noteIsTooLong = input.Note?.Length > 2000;
        var rejectionHasNoReason = !input.Approve && string.IsNullOrWhiteSpace(input.Note);

        if (noteIsTooLong || rejectionHasNoReason)
            throw new ArgumentException("Вкажіть причину відмови (до 2000 символів).");

        if (input.Approve)
        {
            if (input.StoreSlug is null)
                throw new ArgumentException("Некоректна адреса магазину.");

            var storeSlugHasInvalidFormat = !System.Text.RegularExpressions.Regex.IsMatch(input.StoreSlug, "^[a-z0-9][a-z0-9-]{2,99}$");

            if (storeSlugHasInvalidFormat)
                throw new ArgumentException("Некоректна адреса магазину.");

            var user = await db.Users.Include(x => x.Role).SingleAsync(x => x.Id == row.UserId, ct);
            var applicantIsNoLongerAnActiveCustomer = user.Status != UserStatus.Active || user.Role?.Name != Roles.Customer;

            if (applicantIsNoLongerAnActiveCustomer)
                throw new BusinessRuleException("Роль або стан покупця змінилися.");

            var sellerAlreadyExists = await db.Sellers.AnyAsync(x => x.UserId == row.UserId || x.TaxNumber == row.TaxNumber, ct);

            if (sellerAlreadyExists)
                throw new BusinessRuleException("Продавець, податковий номер або адреса магазину вже існують.");

            var storeSlugAlreadyExists = await db.Stores.AnyAsync(x => x.Slug == input.StoreSlug, ct);

            if (storeSlugAlreadyExists)
                throw new BusinessRuleException("Продавець, податковий номер або адреса магазину вже існують.");

            var categories = await db.Categories.Where(x => x.IsActive && (input.AllCategories ? x.ParentId == null : (input.CategoryIds ?? Array.Empty<Guid>()).Contains(x.Id))).Select(x => x.Id).ToListAsync(ct);
            var hasNoCategories = categories.Count == 0;

            if (hasNoCategories)
                throw new ArgumentException("Оберіть активні категорії продавця.");

            var hasUnresolvedCategoryIds = !input.AllCategories
                && categories.Count != (input.CategoryIds ?? []).Distinct().Count();

            if (hasUnresolvedCategoryIds)
                throw new ArgumentException("Оберіть активні категорії продавця.");

            var role = await db.Roles.SingleAsync(x => x.Name == Roles.Seller, ct);
            var now = DateTime.UtcNow;
            var seller = new Seller
            {
                Id = Guid.NewGuid(),
                UserId = row.UserId,
                CompanyName = row.CompanyName,
                TaxNumber = row.TaxNumber,
                Email = row.Email,
                Phone = row.Phone,
                Description = row.Website,
                Status = SellerStatus.ACTIVE,
                CreatedAt = now,
                UpdatedAt = now
            };
            db.Sellers.Add(seller);
            db.Stores.Add(new Store { Id = Guid.NewGuid(), SellerId = seller.Id, Name = row.StoreName, Slug = input.StoreSlug, IsActive = true, CreatedAt = now, UpdatedAt = now });
            db.SellerCategoryGrants.AddRange(categories.Select(category => new SellerCategoryGrant { SellerId = seller.Id, CategoryId = category, IncludeDescendants = true }));
            user.AssignRole(role.Id); // A role change revokes the old customer session; next login applies the new role and MFA.
            row.SellerId = seller.Id;
        }
        row.Status = input.Approve ? "approved" : "rejected";
        row.ReviewNote = input.Note?.Trim();
        row.ReviewedBy = UserId;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        row.Version = Guid.NewGuid();
        // One SaveChanges keeps the review, role, seller, store and category grants atomic.
        await db.SaveChangesAsync(ct);
        return View(row);
    }

    private static SellerApplicationView View(SellerApplication r) => new(r.Id, r.CompanyName, r.StoreName, r.TaxNumber, r.Website,
        r.ContactName, r.Position, r.Email, r.Phone, r.IsNonResident, r.Status, r.ReviewNote, r.CreatedAt, r.UpdatedAt, r.Version, r.SellerId);

    private static void ValidateWebsite(string? website)
    {
        if (website is not { Length: > 0 })
            return;

        var isAbsoluteUri = Uri.TryCreate(website, UriKind.Absolute, out var uri);
        var usesSupportedScheme = isAbsoluteUri && uri!.Scheme is "https" or "http";
        var containsUserInfo = isAbsoluteUri && uri!.UserInfo.Length > 0;

        if (!isAbsoluteUri || !usesSupportedScheme || containsUserInfo)
            throw new ArgumentException("Вкажіть коректну адресу сайту.");
    }
}
