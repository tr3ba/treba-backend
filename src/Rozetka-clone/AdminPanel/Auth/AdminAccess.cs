namespace AdminPanel.Auth;

public static class AdminAccess
{
    public const string StaffRoles = "Administrator,Manager,Moderator,Seller";

    public static bool IsStaff(string? role) => role is "Administrator" or "Manager" or "Moderator" or "Seller";

    public static bool CanAccess(string? role, string path)
    {
        if (!IsStaff(role)) return false;
        var route = "/" + path.Trim('/').ToLowerInvariant();
        if (route is "/" or "/profile") return true;

        if (route.StartsWith("/users", StringComparison.Ordinal)) return role == "Administrator";
        if (route == "/settings") return role == "Administrator";
        if (route.StartsWith("/categories", StringComparison.Ordinal)) return role is "Administrator" or "Manager";
        if (route.StartsWith("/sellers", StringComparison.Ordinal)) return role is "Administrator" or "Manager";
        if (route == "/moderation") return role is "Administrator" or "Manager" or "Moderator";
        if (route is "/products/new" || route.EndsWith("/edit", StringComparison.Ordinal) && route.StartsWith("/products/", StringComparison.Ordinal))
            return role is "Administrator" or "Manager" or "Seller";
        if (route == "/products") return true;
        if (route is "/orders" or "/reviews" or "/support" or "/customer-activity"
            or "/storefront" or "/loyalty") return role == "Administrator";
        return false;
    }
}
