using Contracts.Authentication;

namespace Application.Abstractions;

public interface IRegistrationService
{
    Task<RegistrationMethodsResponse> GetMethodsAsync(CancellationToken ct = default);
    Task<RegistrationChallengeResponse> BeginAsync(RegisterRequest request, CancellationToken ct = default);
    Task<RegistrationChallengeResponse> ResendAsync(Guid challengeId, CancellationToken ct = default);
    Task<AuthResponse> VerifyAsync(VerifyRegistrationRequest request, CancellationToken ct = default);
}
