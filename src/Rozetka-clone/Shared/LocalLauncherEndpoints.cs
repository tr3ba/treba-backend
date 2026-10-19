using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Treba.Development;

public static class LocalLauncherEndpoints
{
    public static void Map(WebApplication app)
    {
        var token = app.Configuration["LocalLaunch:Token"];
        if (!app.Environment.IsDevelopment() || token is null || token.Length < 32) return;
        bool Authorized(HttpContext context)
        {
            if (context.Connection.RemoteIpAddress is not { } ip || !IPAddress.IsLoopback(ip)) return false;
            var supplied = context.Request.Headers["X-Treba-Launcher"].ToString();
            return supplied.Length == token.Length && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(supplied), Encoding.UTF8.GetBytes(token));
        }
        app.MapGet("/__local/status", (HttpContext context) => Authorized(context)
            ? Results.Ok(new { launchId = app.Configuration["LocalLaunch:Id"] }) : Results.NotFound()).ExcludeFromDescription();
        app.MapPost("/__local/stop", (HttpContext context) =>
        {
            if (!Authorized(context)) return Results.NotFound();
            context.Response.OnCompleted(() => { app.Lifetime.StopApplication(); return Task.CompletedTask; });
            return Results.Ok(new { stopping = true });
        }).ExcludeFromDescription();
    }
}
