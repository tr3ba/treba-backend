using Domain.Entities.Users;

namespace Application.Abstractions;

public interface ITokenService
{
    string GenerateAccessToken(User user, string roleName);
    string GenerateRefreshToken();
    DateTimeOffset GetAccessTokenExpiration();
}