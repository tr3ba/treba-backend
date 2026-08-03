using AdminPanel.Services.Abstractions;

namespace AdminPanel.State;

public class OrdersState
{
    public List<OrderDto> Orders { get; private set; } = new();
    public bool IsLoading { get; private set; }
    public int CurrentPage { get; set; } = 1;
    public int TotalPages { get; set; } = 1;
    public string FilterStatus { get; set; } = string.Empty;

    public event Action? OnChange;

    public void SetLoading(bool loading)
    {
        IsLoading = loading;
        NotifyStateChanged();
    }

    public void SetOrders(List<OrderDto> orders, int totalPages)
    {
        Orders = orders;
        TotalPages = totalPages;
        IsLoading = false;
        NotifyStateChanged();
    }

    private void NotifyStateChanged() => OnChange?.Invoke();
}
