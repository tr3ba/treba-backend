using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using System.Security.Claims;

namespace AdminPanel.Layout;

public class AdminRouteGuard : ComponentBase
{
    [Inject] public AuthenticationStateProvider AuthStateProvider { get; set; } = default!;
    [Inject] public NavigationManager NavigationManager { get; set; } = default!;
    [Parameter] public RenderFragment? ChildContent { get; set; }
    [Parameter] public string[] AllowedRoles { get; set; } = new[] { "ADMIN", "Administrator", "MODERATOR", "MANAGER" };

    protected override async Task OnInitializedAsync()
    {
        var authState = await AuthStateProvider.GetAuthenticationStateAsync();
        var user = authState.User;

        if (user.Identity == null || !user.Identity.IsAuthenticated)
        {
            NavigationManager.NavigateTo("/auth/login");
            return;
        }

        var isAuthorized = AllowedRoles.Any(role => user.IsInRole(role) || user.HasClaim(ClaimTypes.Role, role));
        if (!isAuthorized)
        {
            NavigationManager.NavigateTo("/access-denied");
        }
    }
}
