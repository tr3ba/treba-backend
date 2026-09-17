using Contracts.Admin.Users;
using Contracts.Common;

namespace AdminPanel.Services.Abstractions;

public interface IUsersApiClient
{
    Task<UserDetailsResponse> CreateUserAsync(
        CreateUserRequest request,
        CancellationToken cancellationToken = default);

    Task<PagedResponse<UserListItemResponse>> GetUsersAsync(
        int page = 1,
        int size = 20,
        string? search = null,
        string? status = null,
        CancellationToken cancellationToken = default);

    Task<UserDetailsResponse?> GetUserByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<UserStatusResponse> BlockUserAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<UserStatusResponse> UnblockUserAsync(
        Guid id,
        CancellationToken cancellationToken = default);
}
