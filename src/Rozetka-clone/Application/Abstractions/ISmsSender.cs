namespace Application.Abstractions;

public interface ISmsSender
{
    bool IsConfigured { get; }
    Task SendAsync(string recipient, string text, Guid requestId, CancellationToken ct = default);
}

public sealed class SmsDeliveryException : Exception
{
    public SmsDeliveryException() : base("Не вдалося надіслати SMS. Спробуйте пізніше або оберіть пошту.") { }
}
