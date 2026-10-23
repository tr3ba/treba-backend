using System.ComponentModel.DataAnnotations;
using Contracts.Telegram;
using Domain.Entities;
using Domain.Entities.Telegram;
using Domain.Entities.Users;
using Domain.Enums;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Administration;

public sealed class SellerProvisioningService(ApplicationDbContext db)
{
    public async Task<SellerWorkspaceCreatedDto> CreateAsync(CreateSellerWorkspaceRequest request, CancellationToken ct)
    {
        Validator.ValidateObject(request, new(request), true);
        if (request.CategoryIds is null) throw new ArgumentException("Неприпустимий список категорій.");
        var email = request.Email.Trim().ToLowerInvariant();
        if (await db.Users.AnyAsync(u => u.Email == email, ct)) throw new UserDomainException("Обліковий запис із такою електронною адресою вже існує.");
        if (await db.Stores.AnyAsync(s => s.Slug == request.StoreSlug, ct)) throw new UserDomainException("Адреса магазину вже зайнята.");
        var taxNumber = request.TestSeller ? "TEST-" + Guid.NewGuid().ToString("N")[..16].ToUpperInvariant() : request.TaxNumber!.Trim();
        if (await db.Sellers.AnyAsync(s => s.TaxNumber == taxNumber, ct)) throw new UserDomainException("Продавець із таким ЄДРПОУ/ІПН уже існує.");
        var categoryIds = await db.Categories.Where(c => c.IsActive && (request.AllCategories || request.CategoryIds.Contains(c.Id))).Select(c => c.Id).ToListAsync(ct);
        if (categoryIds.Count == 0 || !request.AllCategories && categoryIds.Count != request.CategoryIds.Distinct().Count())
            throw new ArgumentException("Виберіть активні категорії.");
        var role = await db.Roles.SingleAsync(r => r.Name == Roles.Seller, ct);
        await using var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(ct) : null;
        var now = DateTime.UtcNow;
        var user = User.Create(Guid.NewGuid(), email, request.Phone, request.FirstName, request.LastName);
        user.SetPasswordHash(BCrypt.Net.BCrypt.HashPassword(request.Password)); user.AssignRole(role.Id); user.VerifyEmail();
        var description = request.Description?.Trim();
        if (request.TestSeller) description = "Тестовий продавець. Реквізити містять технічну позначку.\n" + description;
        if (description?.Length > 2000) description = description[..2000];
        var seller = new Seller { Id = Guid.NewGuid(), UserId = user.Id, CompanyName = request.CompanyName.Trim(), TaxNumber = taxNumber,
            Description = description, Phone = request.Phone?.Trim(), Email = email, Status = request.Active ? SellerStatus.ACTIVE : SellerStatus.PENDING, CreatedAt = now, UpdatedAt = now };
        var store = new Store { Id = Guid.NewGuid(), SellerId = seller.Id, Name = request.StoreName.Trim(), Slug = request.StoreSlug,
            IsActive = true, CreatedAt = now, UpdatedAt = now };
        db.AddRange(user, seller, store);
        db.SellerCategoryGrants.AddRange(categoryIds.Select(id => new SellerCategoryGrant { SellerId = seller.Id, CategoryId = id, IncludeDescendants = true }));
        await db.SaveChangesAsync(ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
        return new(user.Id, seller.Id, store.Id, email, seller.Status.ToString());
    }
}
