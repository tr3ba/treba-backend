namespace AdminPanel.Auth;

public static class AdminAccess
{
    public const string StaffRoles = "Administrator,Manager,Moderator,Seller";

    public static bool IsStaff(string? role) => role is "Administrator" or "Manager" or "Moderator" or "Seller";

    public static bool CanAccess(string? role, string path)
    {
        if (!IsStaff(role)) return false;
        var route = "/" + path.Trim('/').ToLowerInvariant();
        if (route is "/" or "/profile" or "/notifications") return true;
        if (route == "/products/import") return role == "Seller";

        if (route.StartsWith("/users", StringComparison.Ordinal)) return role == "Administrator";
        if (route == "/settings" || route.StartsWith("/settings/", StringComparison.Ordinal)) return role == "Administrator";
        if (route.StartsWith("/categories", StringComparison.Ordinal)) return role is "Administrator" or "Manager";
        if (route == "/sellers/new") return role == "Administrator";
        if (route.StartsWith("/sellers", StringComparison.Ordinal)) return role is "Administrator" or "Manager";
        if (route == "/moderation") return role is "Administrator" or "Manager" or "Moderator";
        if (route == "/reviews") return role is "Administrator" or "Manager" or "Moderator";
        if (route is "/products/new" || route.EndsWith("/edit", StringComparison.Ordinal) && route.StartsWith("/products/", StringComparison.Ordinal))
            return role is "Administrator" or "Manager" or "Seller";
        if (route == "/products") return true;
        if (route == "/orders" || route.StartsWith("/orders/", StringComparison.Ordinal)) return role is "Administrator" or "Manager" or "Seller";
        if (route == "/support" || route.StartsWith("/support/", StringComparison.Ordinal)) return true;
        if (route is "/customer-activity" or "/loyalty"
            || route.StartsWith("/customer-activity/", StringComparison.Ordinal)
            || route.StartsWith("/loyalty/", StringComparison.Ordinal)) return role is "Administrator" or "Manager";
        return false;
    }
}
