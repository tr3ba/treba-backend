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
        var hasNoUser = actor.UserId is null;
        var hasInsufficientRole = administrator
            ? actor.Role != "Administrator"
            : actor.Role is not ("Administrator" or "Manager" or "Moderator");

        if (hasNoUser || hasInsufficientRole)
            throw new UnauthorizedAccessException();
    }

    public async Task<IReadOnlyList<ReviewReplyTemplateDto>> ListAsync(bool includeDisabled, CancellationToken ct)
    {
        RequireStaff(includeDisabled);

        var rows = await db.ReviewReplyTemplates.AsNoTracking()
            .Where(row => includeDisabled || row.Enabled)
            .OrderBy(row => row.Name)
            .ThenBy(row => row.Id)
            .ToListAsync(ct);

        return rows.Select(View).ToList();
    }

    public async Task<ReviewReplyTemplateDto> SaveAsync(Guid? id, SaveReviewReplyTemplateRequest request, CancellationToken ct)
    {
        RequireStaff(true);

        ValidateRequest(request);
        ValidatePlaceholders(request);

        ReviewReplyTemplate row;

        if (id.HasValue)
        {
            row = await db.ReviewReplyTemplates
                .SingleOrDefaultAsync(value => value.Id == id, ct)
                ?? throw new KeyNotFoundException();

            if (row.Version != request.ExpectedVersion)
                throw new BusinessRuleException("Шаблон змінено в іншому вікні. Оновіть список.");
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

    private static void ValidateRequest(SaveReviewReplyTemplateRequest request)
    {
        var hasInvalidName = string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 80;
        var hasInvalidUkrainianText = string.IsNullOrWhiteSpace(request.TextUk) || request.TextUk.Length > 2000;
        var hasInvalidEnglishText = string.IsNullOrWhiteSpace(request.TextEn) || request.TextEn.Length > 2000;

        if (hasInvalidName || hasInvalidUkrainianText || hasInvalidEnglishText)
            throw new ArgumentException("Вкажіть назву та тексти відповіді UK/EN (до 2000 символів).");
    }

    private static void ValidatePlaceholders(SaveReviewReplyTemplateRequest request)
    {
        foreach (var text in new[] { request.TextUk, request.TextEn })
        {
            var hasUnsupportedPlaceholder = Regex.Matches(text, @"\{[^{}]+\}")
                .Any(match => match.Value is not ("{customer}" or "{product}"));

            if (hasUnsupportedPlaceholder)
                throw new ArgumentException("У шаблоні доступні лише змінні {customer} та {product}.");
        }
    }

    private static ReviewReplyTemplateDto View(ReviewReplyTemplate row) => new(
        row.Id,
        row.Name,
        row.TextUk,
        row.TextEn,
        row.Enabled,
        row.Version,
        row.UpdatedAt);
}
