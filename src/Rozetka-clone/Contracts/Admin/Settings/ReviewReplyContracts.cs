using System.ComponentModel.DataAnnotations;

namespace Contracts.Admin.Settings;

public sealed record ReviewReplyTemplateDto(Guid Id, string Name, string TextUk, string TextEn, bool Enabled, Guid Version, DateTimeOffset UpdatedAt);
public sealed record SaveReviewReplyTemplateRequest(
    [Required, StringLength(80)] string Name,
    [Required, StringLength(2000)] string TextUk,
    [Required, StringLength(2000)] string TextEn,
    bool Enabled = true,
    Guid ExpectedVersion = default);
