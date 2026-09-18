using Application.Products;
using Domain.Entities.Users;

namespace WebApi.Tests;

public class ProductDtoTests
{
    [Fact]
    public void ProductDto_CanBeCreated()
    {
        var product = new ProductDto();

        Assert.Null(product);
    }
}

public class UserStatusTests
{
    [Fact]
    public void Block_SetsBlockedStatus_AndUnblockRestoresActiveStatus()
    {
        var user = User.Create(Guid.NewGuid(), "test@example.com", null, "Test", "User");
        user.VerifyEmail();

        user.Block();
        Assert.Equal(UserStatus.Blocked, user.Status);

        user.Unblock();
        Assert.Equal(UserStatus.Active, user.Status);
    }

    [Fact]
    public void Unblock_WithoutBlock_Throws()
    {
        var user = User.Create(Guid.NewGuid(), "test@example.com", null, "Test", "User");

        Assert.Throws<UserDomainException>(() => user.Unblock());
    }
}
