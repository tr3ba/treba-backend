using AdminPanel.Services.Abstractions;

namespace AdminPanel.State;

public class CategoriesState
{
    public List<CategoryTreeDto> CategoryTree { get; private set; } = new();
    public bool IsLoading { get; private set; }

    public event Action? OnChange;

    public void SetLoading(bool loading)
    {
        IsLoading = loading;
        NotifyStateChanged();
    }

    public void SetCategoryTree(List<CategoryTreeDto> tree)
    {
        CategoryTree = tree;
        IsLoading = false;
        NotifyStateChanged();
    }

    private void NotifyStateChanged() => OnChange?.Invoke();
}
