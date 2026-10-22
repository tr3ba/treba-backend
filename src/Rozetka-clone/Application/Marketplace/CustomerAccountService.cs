using System.Globalization;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Application.Abstractions;
using Application.Common;
using Contracts.Common;
using Contracts.Marketplace;
using Domain.Entities;
using Domain.Entities.Orders;
using Microsoft.EntityFrameworkCore;

namespace Application.Marketplace;

public sealed class CustomerAccountService(IApplicationDbContext db, ICurrentActor actor)
{
    private Guid UserId => actor.UserId ?? throw new UnauthorizedAccessException();
    private static readonly HashSet<string> Fields = ["phone", "lastName", "firstName", "patronymic", "gender", "birthDate", "recipients", "address", "hobbies", "pets", "additionalInfo", "recipientFirstName", "recipientLastName", "recipientPhone", "addressCity", "addressStreet", "addressBuilding", "deliveryMethod", "addressBranch", "additionalInfoOptions"];
    public async Task<CustomerPreferencesView> PreferencesAsync(CancellationToken ct)
    {
        var row = await db.CustomerPreferences.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == UserId, ct);
        var values = JsonSerializer.Deserialize<Dictionary<string, string>>(row?.ValuesJson ?? "{}") ?? [];
        var user = await db.Users.AsNoTracking().Include(x => x.Profile).SingleAsync(x => x.Id == UserId, ct);
        values["firstName"] = user.FirstName; values["lastName"] = user.LastName; values["patronymic"] = user.MiddleName ?? ""; values["phone"] = user.Phone ?? "";
        values["gender"] = user.Profile?.Gender ?? ""; values["birthDate"] = user.Profile?.BirthDate?.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) ?? "";
        return new(values, row?.Version ?? Guid.Empty);
    }

    public async Task<CustomerPreferencesView> SavePreferencesAsync(SaveCustomerPreferencesRequest input, CancellationToken ct)
    {
        if (input.Values is null || input.Values.Count > Fields.Count || input.Values.Any(x => !Fields.Contains(x.Key) || x.Value is null || x.Value.Length > 2000)
            || JsonSerializer.Serialize(input.Values).Length > 16000) throw new ArgumentException("Некоректні дані профілю.");
        var current = await PreferencesAsync(ct);
        var row = await db.CustomerPreferences.SingleOrDefaultAsync(x => x.UserId == UserId, ct);
        if ((row?.Version ?? Guid.Empty) != input.Version) throw new BusinessRuleException("Профіль змінено в іншому вікні. Оновіть сторінку.");
        var merged = current.Values; foreach (var field in input.Values) merged[field.Key] = field.Value.Trim();
        foreach (var field in new[] { "phone", "recipientPhone" })
            if (merged.TryGetValue(field, out var phone) && phone.Length > 0 && (phone.Length > 32 || !new PhoneAttribute().IsValid(phone)))
                throw new ArgumentException("Вкажіть коректний номер телефону.");
        if (merged["firstName"].Length is < 1 or > 100 || merged["lastName"].Length is < 1 or > 100 || merged["patronymic"].Length > 100 || merged["phone"].Length > 32
            || merged["gender"] is not ("" or "male" or "female" or "unspecified")) throw new ArgumentException("Перевірте ім’я, прізвище, телефон і стать.");
        DateOnly? birthDate = null;
        if (merged["birthDate"].Length > 0)
        {
            if (!DateOnly.TryParseExact(merged["birthDate"], ["dd.MM.yyyy", "yyyy-MM-dd"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)) throw new ArgumentException("Вкажіть дату народження у форматі ДД.ММ.РРРР.");
            birthDate = parsed;
        }
        if (row is null) { row = new() { UserId = UserId }; db.CustomerPreferences.Add(row); }
        var user = await db.Users.Include(x => x.Profile).SingleAsync(x => x.Id == UserId, ct);
        user.UpdateProfile(merged["firstName"], merged["lastName"], merged["patronymic"], merged["phone"]);
        if (user.Profile is null) { user.CreateProfile(birthDate, merged["gender"]); db.UserProfiles.Add(user.Profile!); }
        else user.UpdateProfileDetails(birthDate, merged["gender"], user.Profile.AvatarUrl, user.Profile.Language, user.Profile.MarketingEmailsEnabled);
        row.ValuesJson = JsonSerializer.Serialize(merged); row.Version = Guid.NewGuid();
        await db.SaveChangesAsync(ct); return await PreferencesAsync(ct);
    }

    public async Task<PagedResponse<CustomerOrderView>> OrdersAsync(int page, string? search, DateTimeOffset? from, CancellationToken ct)
    {
        if (page is < 1 or > 100000 || search?.Length > 200) throw new ArgumentException("Invalid order filter.");
        var query = db.Orders.AsNoTracking().Where(x => x.CustomerId == UserId);
        if (!string.IsNullOrWhiteSpace(search)) { var term = search.Trim().ToLowerInvariant(); query = query.Where(x => x.OrderNumber.ToLower().Contains(term)); }
        if (from.HasValue) query = query.Where(x => x.CreatedAt >= from.Value);
        var count = await query.CountAsync(ct);
        var orders = await query.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).Skip((page - 1) * 20).Take(20).ToListAsync(ct);
        return new(await ViewsAsync(orders, ct), page, 20, count, (int)Math.Ceiling(count / 20d));
    }

    public async Task<CustomerOrderView?> OrderAsync(Guid id, CancellationToken ct)
    {
        var order = await db.Orders.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.CustomerId == UserId, ct);
        return order is null ? null : (await ViewsAsync([order], ct)).Single();
    }

    private async Task<List<CustomerOrderView>> ViewsAsync(List<Order> orders, CancellationToken ct)
    {
        var ids = orders.Select(x => x.Id).ToArray();
        var lines = await db.OrderItems.AsNoTracking().Where(x => ids.Contains(x.OrderId)).OrderBy(x => x.Id).ToListAsync(ct);
        var productIds = lines.Select(x => x.ProductId).Distinct().ToArray();
        var slugs = await db.Products.AsNoTracking().Where(x => productIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Slug, ct);
        var images = await db.ProductImages.AsNoTracking().Where(x => productIds.Contains(x.ProductId)).OrderByDescending(x => x.IsMain).ThenBy(x => x.SortOrder).ToListAsync(ct);
        var addresses = await db.OrderAddresses.AsNoTracking().Where(x => ids.Contains(x.OrderId)).ToListAsync(ct);
        var history = await db.OrderStatusHistories.AsNoTracking().Where(x => ids.Contains(x.OrderId)).OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).ToListAsync(ct);
        return orders.Select(o => new CustomerOrderView(o.Id, o.OrderNumber, o.Status.ToString(), o.PaymentStatus.ToString(), o.Subtotal,
            o.DiscountAmount, o.DeliveryPrice, o.TotalAmount, o.Currency, o.CreatedAt, o.RecipientName, o.RecipientPhone, o.RecipientEmail,
            addresses.FirstOrDefault(a => a.OrderId == o.Id) is { } a ? string.Join(", ", new[] { a.Country, a.City, a.Street, a.Building, a.Apartment }.Where(x => !string.IsNullOrWhiteSpace(x))) : null,
            lines.Where(x => x.OrderId == o.Id).Select(x => new CustomerOrderItem(x.Id, x.ProductId, slugs.GetValueOrDefault(x.ProductId),
                x.ProductName, x.Quantity, x.UnitPrice, x.TotalPrice, images.FirstOrDefault(i => i.ProductId == x.ProductId)?.ImageUrl)).ToList(),
            history.Where(x => x.OrderId == o.Id).Select(x => new CustomerOrderEvent(x.NewStatus.ToString(), x.CreatedAt)).ToList())).ToList();
    }
}
