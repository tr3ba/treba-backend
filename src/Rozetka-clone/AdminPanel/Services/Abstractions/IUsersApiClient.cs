using AdminPanel.Infrastructure;

namespace AdminPanel.Services.Abstractions;

public interface IUsersApiClient
{
    Task<PagedResult<UserDto>> GetUsersAsync(int page = 1, int size = 20, string? search = null);
    Task<ApiResponse<UserDto>> GetUserByIdAsync(Guid id);
    Task<ApiResponse<bool>> BlockUserAsync(Guid id);
    Task<ApiResponse<bool>> UnblockUserAsync(Guid id);
}

public record UserDto(
    Guid Id, 
    string Email, 
    string Phone, 
    string FirstName, 
    string LastName, 
    string Status, 
    List<string> Roles, 
    DateTime CreatedAt, 
    DateTime? LastLoginAt
);
