using Domain.Entities.Users;
using Infrastructure.Authentication;

namespace WebApi.Tests;

public sealed class SessionTokenBindingTests
{
    private static User Account()
    {
        var user = User.Create(Guid.NewGuid(), "staff@example.test", null, "Staff", "Member");
        user.AssignRole(Guid.NewGuid()); user.VerifyEmail();
        user.SetRefreshToken("private-random-refresh-token", DateTimeOffset.UtcNow.AddDays(1));
        return user;
    }

    [Fact]
    public void CurrentToken_ValidatesWithoutDisclosingRefreshToken()
    {
        var user = Account(); var claim = SessionTokenBinding.Create(user);
        Assert.True(SessionTokenBinding.IsCurrent(claim, user));
        Assert.DoesNotContain(user.RefreshToken!, claim);
        Assert.False(SessionTokenBinding.IsCurrent(null, user));
    }

    [Fact]
    public void UnblockingAccount_DoesNotRestoreRevokedAccessToken()
    {
        var user = Account(); var claim = SessionTokenBinding.Create(user);
        user.Block(); user.Unblock();
        Assert.False(SessionTokenBinding.IsCurrent(claim, user));
    }

    [Fact]
    public void RestoringOldRole_DoesNotRestoreRevokedAccessToken()
    {
        var user = Account(); var original = user.RoleId; var claim = SessionTokenBinding.Create(user);
        user.AssignRole(Guid.NewGuid()); user.AssignRole(original);
        Assert.False(SessionTokenBinding.IsCurrent(claim, user));
    }

    [Fact]
    public void NewSession_InvalidatesOldAccessToken()
    {
        var user = Account(); var old = SessionTokenBinding.Create(user);
        user.SetRefreshToken("a-new-private-refresh-token", DateTimeOffset.UtcNow.AddDays(1));
        Assert.False(SessionTokenBinding.IsCurrent(old, user));
        Assert.True(SessionTokenBinding.IsCurrent(SessionTokenBinding.Create(user), user));
    }
}
