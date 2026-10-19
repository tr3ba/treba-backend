using System.Security.Claims;
using Application.Abstractions;

namespace WebApi.Authentication;

public sealed class HttpCurrentActor(IHttpContextAccessor accessor) : ICurrentActor
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;
    public Guid? UserId => Guid.TryParse(Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
    public string Name => string.Join(" ", new[] { Principal?.FindFirstValue("firstName"), Principal?.FindFirstValue("lastName") }
        .Where(value => !string.IsNullOrWhiteSpace(value))) is { Length: > 0 } name
        ? name : Principal?.FindFirstValue(ClaimTypes.Email) ?? "";
    public string Role => Principal?.FindFirstValue(ClaimTypes.Role) ?? "";
}
