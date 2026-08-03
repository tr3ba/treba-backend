using AdminPanel.Services.Abstractions;

namespace AdminPanel.State;

public class UsersState
{
    public List<UserDto> Users { get; private set; } = new();
    public bool IsLoading { get; private set; }
    public int CurrentPage { get; set; } = 1;
    public int TotalPages { get; set; } = 1;
    public string SearchQuery { get; set; } = string.Empty;

    public event Action? OnChange;

    public void SetLoading(bool loading)
    {
        IsLoading = loading;
        NotifyStateChanged();
    }

    public void SetUsers(List<UserDto> users, int totalPages)
    {
        Users = users;
        TotalPages = totalPages;
        IsLoading = false;
        NotifyStateChanged();
    }

    private void NotifyStateChanged() => OnChange?.Invoke();
}
