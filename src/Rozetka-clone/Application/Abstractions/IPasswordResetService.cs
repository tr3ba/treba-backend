using Contracts.Authentication;

namespace Application.Abstractions;

public interface IPasswordResetService
{
    Task<PasswordResetChallengeResponse> RequestAsync(RequestPasswordResetRequest request, CancellationToken ct = default);
    Task CompleteAsync(CompletePasswordResetRequest request, CancellationToken ct = default);
}
