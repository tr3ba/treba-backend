using AdminPanel.Services.Implementations;

namespace AdminPanel.State;

public sealed class ReviewModerationState(ReviewsApiClient reviews)
{
    public int PendingCount { get; private set; }
    public event Action? Changed;

    public void SetPending(int count)
    {
        count = Math.Max(0, count);
        if (PendingCount == count) return;
        PendingCount = count;
        Changed?.Invoke();
    }

    public async Task RefreshAsync() => SetPending(await reviews.PendingCountAsync());
}
