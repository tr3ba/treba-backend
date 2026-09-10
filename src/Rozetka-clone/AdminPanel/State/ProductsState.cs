using AdminPanel.Services.Abstractions;

namespace AdminPanel.State;

public class ProductsState
{
    public List<ProductListItemDto> PendingProducts { get; private set; } = new();
    public bool IsLoading { get; private set; }
    public int CurrentPage { get; set; } = 1;
    public int TotalPages { get; set; } = 1;

    public event Action? OnChange;

    public void SetLoading(bool loading)
    {
        IsLoading = loading;
        NotifyStateChanged();
    }

    public void SetPendingProducts(List<ProductListItemDto> products, int totalPages)
    {
        PendingProducts = products;
        TotalPages = totalPages;
        IsLoading = false;
        NotifyStateChanged();
    }

    private void NotifyStateChanged() => OnChange?.Invoke();
}
