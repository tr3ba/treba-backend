using System.Net.Http.Headers;

namespace AdminPanel.Auth;

public class AuthorizedHttpMessageHandler : DelegatingHandler
{
    private readonly TokenStorageService _tokenStorage;

    public AuthorizedHttpMessageHandler(TokenStorageService tokenStorage)
    {
        _tokenStorage = tokenStorage;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await _tokenStorage.GetTokenAsync();
        if (!string.IsNullOrEmpty(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
