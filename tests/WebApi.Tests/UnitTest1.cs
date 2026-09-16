using Application.Products;

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
