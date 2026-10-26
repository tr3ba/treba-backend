namespace Application.Abstractions;

public interface ITransactionalEmailSender
{
    Task SendNotificationAsync(string recipient, string title, string text, string url, CancellationToken ct);
}
