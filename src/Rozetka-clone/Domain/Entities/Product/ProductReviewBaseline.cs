namespace Domain.Entities.Product;

public sealed class ProductReviewBaseline
{
    public Guid ProductId
    {
        get; set;
    }
    public DateTimeOffset CapturedAt { get; set; } = DateTimeOffset.UtcNow;
    public string Snapshot { get; set; } = "{}";
}
