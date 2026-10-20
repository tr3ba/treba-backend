using Microsoft.JSInterop;

namespace AdminPanel.Auth;

public class TokenStorageService
{
    private readonly IJSRuntime _jsRuntime;
    private const string TokenKey = "admin_auth_token";
    private const string RefreshTokenKey = "admin_refresh_token";
    private string? siteAccess, siteRefresh;
    public bool IsWebsiteSession { get; private set; }

    public TokenStorageService(IJSRuntime jsRuntime, IHttpContextAccessor? context = null, IConfiguration? configuration = null)
    {
        _jsRuntime = jsRuntime;
        if (configuration?.GetValue<bool>("WebsiteLogin:Enabled") == true)
        {
            siteAccess = context?.HttpContext?.User.FindFirst(WebsiteLogin.AccessClaim)?.Value;
            siteRefresh = context?.HttpContext?.User.FindFirst(WebsiteLogin.RefreshClaim)?.Value;
        }
        IsWebsiteSession = siteAccess is not null;
    }

    public async ValueTask<string?> GetTokenAsync()
    {
        if (IsWebsiteSession) return siteAccess;
        try
        {
            return await _jsRuntime.InvokeAsync<string?>("localStorage.getItem", TokenKey);
        }
        catch
        {
            return null;
        }
    }

    public async ValueTask SetTokenAsync(string token)
    {
        if (IsWebsiteSession) { siteAccess = token; return; }
        await _jsRuntime.InvokeVoidAsync("localStorage.setItem", TokenKey, token);
    }

    public async ValueTask RemoveTokenAsync()
    {
        await _jsRuntime.InvokeVoidAsync("localStorage.removeItem", TokenKey);
    }

    public async ValueTask<string?> GetRefreshTokenAsync()
    {
        if (IsWebsiteSession) return siteRefresh;
        try
        {
            return await _jsRuntime.InvokeAsync<string?>("localStorage.getItem", RefreshTokenKey);
        }
        catch
        {
            return null;
        }
    }

    public async ValueTask SetRefreshTokenAsync(string refreshToken)
    {
        if (IsWebsiteSession) { siteRefresh = refreshToken; return; }
        await _jsRuntime.InvokeVoidAsync("localStorage.setItem", RefreshTokenKey, refreshToken);
    }

    public async ValueTask ClearTokensAsync()
    {
        if (IsWebsiteSession) { await _jsRuntime.InvokeVoidAsync("trebaSiteLogin.logout"); siteAccess = siteRefresh = null; return; }
        await RemoveTokenAsync();
        await _jsRuntime.InvokeVoidAsync("localStorage.removeItem", RefreshTokenKey);
    }
}
