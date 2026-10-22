using Application.Common;
using Application.Orders;
using Contracts.Admin.Operations;
using Contracts.Common;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Application.Administration;

public sealed partial class SupportService
{
    public async Task<TicketCapabilities> CapabilitiesAsync(CancellationToken ct) => new(
        OperationsStaff && actor.UserId.HasValue && await db.FeatureFlags.AsNoTracking().AnyAsync(x => x.Key == FeatureFlag.ChatDeletion && x.Enabled, ct), OperationsStaff && actor.UserId.HasValue);

    public async Task<PagedResponse<TicketRow>> DeletedAsync(int page, string? search, CancellationToken ct)
    {
        if (!OperationsStaff || actor.UserId is null) throw new UnauthorizedAccessException();
        OrderService.ValidatePage(page, 12);
        var query = Visible.IgnoreQueryFilters().AsNoTracking().Where(t => t.IsDeleted);
        if (search?.Length > 200) throw new ArgumentException("Пошуковий запит надто довгий.");
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(t => t.Subject.ToLower().Contains(term)
                || db.Users.Any(u => u.Id == t.CustomerId && (u.FirstName.ToLower().Contains(term) || u.LastName.ToLower().Contains(term) || u.Email.ToLower().Contains(term)))
                || db.Orders.Any(o => o.Id == t.OrderId && o.OrderNumber.ToLower().Contains(term)));
        }
        var count = await query.CountAsync(ct);
        var items = await Rows(query.OrderByDescending(t => t.DeletedAt).ThenBy(t => t.Id).Skip((page - 1) * 12).Take(12)).ToListAsync(ct);
        return new(items, page, 12, count, (int)Math.Ceiling(count / 12d));
    }

    public async Task DeleteAsync(Guid id, TicketDeletionRequest request, CancellationToken ct)
    {
        if (!OperationsStaff || actor.UserId is null) throw new UnauthorizedAccessException();
        if (!(await CapabilitiesAsync(ct)).CanDelete) throw new BusinessRuleException("Видалення чатів вимкнено в налаштуваннях функцій.");
        var ticket = await Visible.SingleOrDefaultAsync(t => t.Id == id, ct) ?? throw new KeyNotFoundException();
        if (ticket.Version != request.ExpectedVersion) throw new BusinessRuleException("Переписку змінено. Оновіть її перед видаленням.");
        if (ticket.Kind == "RETURN" && ticket.Status != "CLOSED") throw new BusinessRuleException("Спочатку завершіть обробку повернення.");
        ticket.IsDeleted = true; ticket.DeletedAt = DateTimeOffset.UtcNow; ticket.DeletedBy = actor.UserId;
        ticket.UpdatedAt = ticket.DeletedAt.Value; ticket.Version = Guid.NewGuid();
        await db.SaveChangesAsync(ct);
    }

    public async Task RestoreAsync(Guid id, TicketDeletionRequest request, CancellationToken ct)
    {
        if (!OperationsStaff || actor.UserId is null) throw new UnauthorizedAccessException();
        var ticket = await Visible.IgnoreQueryFilters().SingleOrDefaultAsync(t => t.Id == id && t.IsDeleted, ct) ?? throw new KeyNotFoundException();
        if (ticket.Version != request.ExpectedVersion) throw new BusinessRuleException("Переписку вже відновлено або змінено. Оновіть корзину.");
        if (ticket.Status != "CLOSED" && ticket.Kind == "CHAT"
            && await db.SupportTickets.AnyAsync(t => t.CustomerId == ticket.CustomerId && t.Kind == "CHAT" && t.Status != "CLOSED", ct))
            throw new BusinessRuleException("У покупця вже є новий активний чат. Спочатку завершіть його, потім відновіть цю переписку.");
        ticket.IsDeleted = false; ticket.DeletedAt = null; ticket.DeletedBy = null;
        ticket.UpdatedAt = DateTimeOffset.UtcNow; ticket.Version = Guid.NewGuid();
        await db.SaveChangesAsync(ct);
    }
}
