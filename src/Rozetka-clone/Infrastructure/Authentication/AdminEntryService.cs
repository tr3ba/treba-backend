using System.Security.Cryptography;
using System.Text;
using Application.Abstractions;
using Contracts.Authentication;
using Domain.Entities.Users;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Authentication;

public sealed class AdminEntryService(IApplicationDbContext db, ITokenService tokens)
{
    public async Task<AdminEntryResponse> CreateAsync(Guid userId, CancellationToken ct)
    {
        var user = await Staff(userId, ct);
        var secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var challenge = AuthenticationChallenge.Create(user.Id, AuthenticationChallengePurpose.AdminEntry, DateTimeOffset.UtcNow.AddMinutes(1));
        challenge.SetCode(Hash(secret) + "." + SessionTokenBinding.Create(user), challenge.ExpiresAt, DateTimeOffset.UtcNow);
        db.AuthenticationChallenges.Add(challenge); await db.SaveChangesAsync(ct);
        return new(challenge.Id.ToString("N") + "." + secret, challenge.ExpiresAt);
    }

    public async Task<AuthResponse> RedeemAsync(string code, CancellationToken ct)
    {
        var parts = code.Split('.');
        if (parts.Length != 2 || !Guid.TryParseExact(parts[0], "N", out var id) || parts[1].Length != 64) throw new UnauthorizedAccessException();
        var challenge = await db.AuthenticationChallenges.SingleOrDefaultAsync(c => c.Id == id, ct);
        if (challenge is null || challenge.Purpose != AuthenticationChallengePurpose.AdminEntry || !challenge.IsActive(DateTimeOffset.UtcNow)) throw new UnauthorizedAccessException();
        var user = await Staff(challenge.UserId, ct);
        var expected = Hash(parts[1]) + "." + SessionTokenBinding.Create(user);
        if (challenge.CodeHash is null || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(challenge.CodeHash))) throw new UnauthorizedAccessException();
        challenge.Consume(); await db.SaveChangesAsync(ct);
        return new(user.Id, user.Email, user.FirstName, user.LastName, user.Role!.Name, tokens.GenerateAccessToken(user, user.Role.Name), user.RefreshToken!, tokens.GetAccessTokenExpiration());
    }

    private async Task<User> Staff(Guid id, CancellationToken ct)
    {
        var user = await db.Users.Include(u => u.Role).SingleOrDefaultAsync(u => u.Id == id, ct);
        if (user is null || user.IsDeleted || user.Status != UserStatus.Active || user.Role?.Name is not (Roles.Administrator or Roles.Manager or Roles.Moderator or Roles.Seller)
            || string.IsNullOrEmpty(user.RefreshToken) || user.RefreshTokenExpiryTime is null || user.RefreshTokenExpiryTime <= DateTimeOffset.UtcNow) throw new UnauthorizedAccessException();
        return user;
    }
    private static string Hash(string secret) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
}
