using System.Security.Cryptography;
using System.Text;
using Domain.Entities.Users;

namespace Infrastructure.Authentication;

public static class SessionTokenBinding
{
    public const string ClaimName = "session";
    public static string Create(User user) => string.IsNullOrEmpty(user.RefreshToken)
        ? throw new InvalidOperationException("The login session is missing.")
        : Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(user.RefreshToken)));

    public static bool IsCurrent(string? claim, User user)
    {
        if (string.IsNullOrEmpty(claim) || string.IsNullOrEmpty(user.RefreshToken)) return false;
        var expected = Create(user);
        return claim.Length == expected.Length && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(claim), Encoding.UTF8.GetBytes(expected));
    }
}
