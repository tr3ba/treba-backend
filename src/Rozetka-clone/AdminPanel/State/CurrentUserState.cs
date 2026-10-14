using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text.Json;
using Contracts.Admin.Users;
using Microsoft.AspNetCore.Components.Forms;

namespace AdminPanel.State;

public sealed class CurrentUserState(HttpClient httpClient)
{
    private const long MaxAvatarBytes = 5 * 1024 * 1024;
    public UserDetailsResponse? User { get; private set; }
    public string? AvatarDataUrl { get; private set; }

    public event Action? Changed;

    public async Task<UserDetailsResponse?> LoadAsync(bool force = false, CancellationToken cancellationToken = default)
    {
        if (User is not null && !force)
        {
            return User;
        }

        User = await httpClient.GetFromJsonAsync<UserDetailsResponse>("api/users/me", cancellationToken);
        await LoadAvatarDataUrlAsync(cancellationToken);
        Changed?.Invoke();
        return User;
    }

    public async Task<UserDetailsResponse> UpdateAsync(
        UpdateCurrentUserRequest request,
        CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PatchAsJsonAsync("api/users/me", request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(await ReadErrorAsync(response, "Не вдалося зберегти профіль.", cancellationToken));
        }

        User = await response.Content.ReadFromJsonAsync<UserDetailsResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("API повернуло порожню відповідь.");
        Changed?.Invoke();
        return User;
    }

    public async Task<UserDetailsResponse> UploadAvatarAsync(
        IBrowserFile avatar,
        CancellationToken cancellationToken = default)
    {
        await using var stream = avatar.OpenReadStream(MaxAvatarBytes, cancellationToken);
        using var fileContent = new StreamContent(stream);
        if (MediaTypeHeaderValue.TryParse(avatar.ContentType, out var contentType))
            fileContent.Headers.ContentType = contentType;

        using var form = new MultipartFormDataContent();
        form.Add(fileContent, "avatar", avatar.Name);
        using var response = await httpClient.PostAsync("api/users/me/avatar", form, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(await ReadErrorAsync(response, "Не вдалося завантажити аватарку.", cancellationToken));

        User = await response.Content.ReadFromJsonAsync<UserDetailsResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("API повернуло порожню відповідь.");
        await LoadAvatarDataUrlAsync(cancellationToken);
        Changed?.Invoke();
        return User;
    }

    public async Task<UserDetailsResponse> DeleteAvatarAsync(CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.DeleteAsync("api/users/me/avatar", cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(await ReadErrorAsync(response, "Не вдалося видалити аватарку.", cancellationToken));

        User = await response.Content.ReadFromJsonAsync<UserDetailsResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("API повернуло порожню відповідь.");
        AvatarDataUrl = null;
        Changed?.Invoke();
        return User;
    }

    public void Clear()
    {
        User = null;
        AvatarDataUrl = null;
        Changed?.Invoke();
    }

    private async Task LoadAvatarDataUrlAsync(CancellationToken cancellationToken)
    {
        AvatarDataUrl = null;
        var avatarUrl = User?.Profile?.AvatarUrl;
        if (string.IsNullOrWhiteSpace(avatarUrl) || !avatarUrl.StartsWith("/uploads/avatars/", StringComparison.OrdinalIgnoreCase))
            return;

        try
        {
            var bytes = await httpClient.GetByteArrayAsync(avatarUrl.TrimStart('/'), cancellationToken);
            var mediaType = Path.GetExtension(avatarUrl).ToLowerInvariant() switch
            {
                ".png" => "image/png",
                ".gif" => "image/gif",
                ".webp" => "image/webp",
                _ => "image/jpeg"
            };
            AvatarDataUrl = $"data:{mediaType};base64,{Convert.ToBase64String(bytes)}";
        }
        catch (HttpRequestException)
        {
            AvatarDataUrl = null;
        }
    }

    private static async Task<string> ReadErrorAsync(
        HttpResponseMessage response,
        string fallback,
        CancellationToken cancellationToken)
    {
        try
        {
            using var document = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken),
                cancellationToken: cancellationToken);
            var root = document.RootElement;
            foreach (var propertyName in new[] { "message", "detail", "title" })
            {
                if (root.TryGetProperty(propertyName, out var property) && !string.IsNullOrWhiteSpace(property.GetString()))
                    return property.GetString()!;
            }

            if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Object)
            {
                foreach (var error in errors.EnumerateObject())
                {
                    if (error.Value.ValueKind == JsonValueKind.Array && error.Value.GetArrayLength() > 0)
                        return error.Value[0].GetString() ?? fallback;
                }
            }
        }
        catch (JsonException) { }

        return fallback;
    }
}
