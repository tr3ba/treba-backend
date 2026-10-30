using System.Text.RegularExpressions;
using Application.Abstractions;
using Application.Common;
using Contracts.Admin.Settings;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Application.Administration;

public sealed class ReviewReplyTemplateService(IApplicationDbContext db, ICurrentActor actor)
{
    private void RequireStaff(bool administrator = false)
    {
        if (actor.UserId is null || (administrator ? actor.Role != "Administrator" : actor.Role is not ("Administrator" or "Manager" or "Moderator")))
            throw new UnauthorizedAccessException();
    }

    public async Task<IReadOnlyList<ReviewReplyTemplateDto>> ListAsync(bool includeDisabled, CancellationToken ct)
    {
        RequireStaff(includeDisabled);
        var rows = await db.ReviewReplyTemplates.AsNoTracking().Where(row => includeDisabled || row.Enabled).OrderBy(row => row.Name).ThenBy(row => row.Id).ToListAsync(ct);
        return rows.Select(View).ToList();
    }

    public async Task<ReviewReplyTemplateDto> SaveAsync(Guid? id, SaveReviewReplyTemplateRequest request, CancellationToken ct)
    {
        RequireStaff(true);
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 80 || string.IsNullOrWhiteSpace(request.TextUk) || request.TextUk.Length > 2000 || string.IsNullOrWhiteSpace(request.TextEn) || request.TextEn.Length > 2000)
            throw new ArgumentException("Вкажіть назву та тексти відповіді UK/EN (до 2000 символів).");
        foreach (var text in new[] { request.TextUk, request.TextEn })
            if (Regex.Matches(text, @"\{[^{}]+\}").Any(match => match.Value is not ("{customer}" or "{product}")))
                throw new ArgumentException("У шаблоні доступні лише змінні {customer} та {product}.");
        ReviewReplyTemplate row;
        if (id.HasValue)
        {
            row = await db.ReviewReplyTemplates.SingleOrDefaultAsync(value => value.Id == id, ct) ?? throw new KeyNotFoundException();
            if (row.Version != request.ExpectedVersion) throw new BusinessRuleException("Шаблон змінено в іншому вікні. Оновіть список.");
        }
        else
        {
            row = new();
            db.ReviewReplyTemplates.Add(row);
        }
        row.Name = request.Name.Trim();
        row.TextUk = request.TextUk.Trim();
        row.TextEn = request.TextEn.Trim();
        row.Enabled = request.Enabled;
        row.Version = Guid.NewGuid();
        row.UpdatedAt = DateTimeOffset.UtcNow;
        row.UpdatedBy = actor.UserId!.Value;
        await db.SaveChangesAsync(ct);
        return View(row);
    }

    private static ReviewReplyTemplateDto View(ReviewReplyTemplate row) => new(row.Id, row.Name, row.TextUk, row.TextEn, row.Enabled, row.Version, row.UpdatedAt);
}
