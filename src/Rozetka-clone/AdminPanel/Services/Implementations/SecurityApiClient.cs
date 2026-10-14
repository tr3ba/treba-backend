using System.Net.Http.Json;
using System.Text.Json;
using AdminPanel.Infrastructure;
using Contracts.Authentication;

namespace AdminPanel.Services.Implementations;

public sealed class SecurityApiClient(HttpClient httpClient)
{
    public Task<AccountSecuritySettingsResponse> GetAsync(CancellationToken cancellationToken = default) =>
        GetRequiredAsync<AccountSecuritySettingsResponse>("api/security", cancellationToken);

    public Task<AuthenticatorSetupResponse> BeginAuthenticatorAsync(CancellationToken cancellationToken = default) =>
        PostRequiredAsync<object, AuthenticatorSetupResponse>("api/security/authenticator/setup", new { }, cancellationToken);

    public Task<AccountSecuritySettingsResponse> ConfirmAuthenticatorAsync(string code, CancellationToken cancellationToken = default) =>
        PostRequiredAsync<ConfirmAuthenticatorRequest, AccountSecuritySettingsResponse>("api/security/authenticator/confirm", new(code), cancellationToken);

    public Task<AccountSecuritySettingsResponse> DisableAuthenticatorAsync(string code, CancellationToken cancellationToken = default) =>
        PostRequiredAsync<ConfirmAuthenticatorRequest, AccountSecuritySettingsResponse>("api/security/authenticator/disable", new(code), cancellationToken);

    public Task<StartEmailTwoFactorResponse> BeginEmailAsync(CancellationToken cancellationToken = default) =>
        PostRequiredAsync<object, StartEmailTwoFactorResponse>("api/security/email/setup", new { }, cancellationToken);

    public Task<AccountSecuritySettingsResponse> ConfirmEmailAsync(Guid challengeId, string code, CancellationToken cancellationToken = default) =>
        PostRequiredAsync<ConfirmEmailTwoFactorRequest, AccountSecuritySettingsResponse>("api/security/email/confirm", new(challengeId, code), cancellationToken);

    public Task<StartEmailTwoFactorResponse> BeginEmailDisableAsync(CancellationToken cancellationToken = default) =>
        PostRequiredAsync<object, StartEmailTwoFactorResponse>("api/security/email/disable/start", new { }, cancellationToken);

    public Task<AccountSecuritySettingsResponse> ConfirmEmailDisableAsync(Guid challengeId, string code, CancellationToken cancellationToken = default) =>
        PostRequiredAsync<ConfirmEmailTwoFactorRequest, AccountSecuritySettingsResponse>("api/security/email/disable/confirm", new(challengeId, code), cancellationToken);

    private async Task<T> GetRequiredAsync<T>(string url, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(url, cancellationToken);
        return await ReadRequiredAsync<T>(response, cancellationToken);
    }

    private async Task<TResponse> PostRequiredAsync<TRequest, TResponse>(string url, TRequest request, CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync(url, request, cancellationToken);
        return await ReadRequiredAsync<TResponse>(response, cancellationToken);
    }

    private static async Task<T> ReadRequiredAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var message = response.StatusCode switch
            {
                System.Net.HttpStatusCode.Unauthorized => "Your session has expired. Sign in again.",
                System.Net.HttpStatusCode.NotFound => "The security API endpoint was not found. Restart the backend after rebuilding it.",
                System.Net.HttpStatusCode.InternalServerError => "The backend could not complete the security operation. Check its logs and configuration.",
                _ => $"The security operation failed (HTTP {(int)response.StatusCode})."
            };
            try
            {
                using var json = JsonDocument.Parse(body);
                var root = json.RootElement;
                message = GetStringIgnoreCase(root, "message")
                    ?? GetStringIgnoreCase(root, "detail")
                    ?? GetStringIgnoreCase(root, "title")
                    ?? message;
            }
            catch (JsonException) { }
            throw new ApiException(message, (int)response.StatusCode);
        }

        return await response.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken)
            ?? throw new ApiException("The API returned an empty response.", 502);
    }

    private static string? GetStringIgnoreCase(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        foreach (var property in element.EnumerateObject())
        {
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
                && property.Value.ValueKind == JsonValueKind.String)
                return property.Value.GetString();
        }
        return null;
    }
}
