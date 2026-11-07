using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Application.Abstractions;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Infrastructure.Authentication;

public sealed class TurboSmsOptions
{
    public const string SectionName = "TurboSms";

    public string Token { get; set; } = string.Empty;
}

public sealed class TurboSmsSender(
    HttpClient http, IApplicationDbContext db, IOptions<TurboSmsOptions> options) : ISmsSender
{
    public const string Sender = "MAGAZIN";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(options.Value.Token);

    public async Task SendAsync(string recipient, string text, Guid requestId, CancellationToken ct = default)
    {
        // Always check persisted settings immediately before any billable provider request.
        if (!await db.FeatureFlags.AsNoTracking().AnyAsync(x => x.Key == FeatureFlag.SmsSending && x.Enabled, ct))
            throw new InvalidOperationException("SMS вимкнено. Оберіть підтвердження поштою.");

        if (!IsConfigured)
            throw new InvalidOperationException("SMS тимчасово недоступні. Оберіть підтвердження поштою.");

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.turbosms.ua/message/send.json");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.Value.Token.Trim());
        request.Content = JsonContent.Create(new
        {
            sequence_id = requestId.ToString("N"),
            recipients = new[] { NormalizePhone(recipient) },
            sms = new
            {
                sender = Sender,
                text
            },
        });
        try
        {
            // No automatic retries: a lost HTTP response can still represent a charged SMS.
            using var response = await http.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
                throw new SmsDeliveryException();

            using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            var root = json.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
                throw new SmsDeliveryException();

            if (!root.TryGetProperty("response_code", out var overall))
                throw new SmsDeliveryException();

            if (overall.ValueKind != JsonValueKind.Number || !overall.TryGetInt32(out var code))
                throw new SmsDeliveryException();

            var overallCodeIsAccepted = code is 0 or 800 or 801 or 802;

            if (!overallCodeIsAccepted)
                throw new SmsDeliveryException();

            if (!root.TryGetProperty("response_result", out var results))
                throw new SmsDeliveryException();

            if (results.ValueKind != JsonValueKind.Array || results.GetArrayLength() != 1)
                throw new SmsDeliveryException();

            var result = results[0];

            if (result.ValueKind != JsonValueKind.Object)
                throw new SmsDeliveryException();

            if (!result.TryGetProperty("response_code", out var recipientCode))
                throw new SmsDeliveryException();

            if (recipientCode.ValueKind != JsonValueKind.Number
                || !recipientCode.TryGetInt32(out var status)
                || status != 0)
                throw new SmsDeliveryException();

            if (!result.TryGetProperty("message_id", out var id))
                throw new SmsDeliveryException();

            var messageIdIsValid = id.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(id.GetString());

            if (!messageIdIsValid)
                throw new SmsDeliveryException();
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            // Never put provider bodies, credentials or security codes in application errors/logs.
            throw new SmsDeliveryException();
        }
    }

    public static string NormalizePhone(string phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
            throw new ArgumentException("Вкажіть номер телефону в міжнародному форматі, наприклад +380671234567.");

        var hasInvalidCharacter = phone.Any(character =>
            !(character is >= '0' and <= '9' or '+' or ' ' or '-' or '(' or ')'));

        if (hasInvalidCharacter)
            throw new ArgumentException("Вкажіть номер телефону в міжнародному форматі, наприклад +380671234567.");

        var compact = phone.Replace(" ", "").Replace("-", "").Replace("(", "").Replace(")", "");

        if (compact.StartsWith('+'))
            compact = compact[1..];

        if (compact.Length == 10 && compact.StartsWith('0'))
            compact = "38" + compact;

        var hasInvalidLength = compact.Length is < 8 or > 15;
        var hasInvalidPrefix = compact.Length > 0 && compact[0] == '0';
        var hasInvalidDigit = compact.Any(character => character is < '0' or > '9');
        var ukrainianNumberHasInvalidLength = compact.StartsWith("380") && compact.Length != 12;

        if (hasInvalidLength || hasInvalidPrefix || hasInvalidDigit || ukrainianNumberHasInvalidLength)
            throw new ArgumentException("Вкажіть номер телефону в міжнародному форматі, наприклад +380671234567.");
        return compact;
    }
}
