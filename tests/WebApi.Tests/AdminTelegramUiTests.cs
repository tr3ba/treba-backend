using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using AdminPanel.Components;
using AdminPanel.Services.Implementations;
using AngleSharp.Html.Parser;
using Contracts.Telegram;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace WebApi.Tests;

public sealed class AdminTelegramUiTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Login_ShowsUsableGuardOnlyWhenConfigured_WithoutSetupInstructions(bool configured)
    {
        using var api = new FixtureApi(new TelegramConfigurationDto(configured, configured ? "fixture_bot" : null, null));
        var document = new HtmlParser().ParseDocument(await Render<GuardLoginPanel>(api));
        var panel = document.QuerySelector(".guard-login-panel");
        if (configured)
        {
            Assert.NotNull(panel);
            Assert.False(panel.QuerySelector("button")!.HasAttribute("disabled"));
        }
        else Assert.Null(panel);
        Assert.DoesNotContain("TelegramNotConfigured", document.Body!.TextContent);
    }

    [Theory]
    [InlineData("Seller")]
    [InlineData("Moderator")]
    public async Task Profile_LinkedAccountShowsItsIdentity_WithoutLinkingOrRefreshInstructions(string role)
    {
        using var api = new FixtureApi(new TelegramLinkDto(true, true, "fixture_account", 12345, DateTimeOffset.UtcNow, null, null, null));
        var document = new HtmlParser().ParseDocument(await Render<TelegramProfilePanel>(api, role));
        Assert.Contains("@fixture_account", document.QuerySelector(".feedback-success")!.TextContent);
        Assert.Single(document.QuerySelectorAll("button"));
        Assert.Null(document.QuerySelector(".telegram-profile-panel small"));
        Assert.Empty(document.QuerySelectorAll("a"));
    }

    [Fact]
    public async Task Profile_UnlinkedAccountStartsInTheProfile_WithOneAction()
    {
        using var api = new FixtureApi(new TelegramLinkDto(true, false, null, null, null, null, null, null));
        var document = new HtmlParser().ParseDocument(await Render<TelegramProfilePanel>(api));
        var button = Assert.Single(document.QuerySelectorAll("button"));
        Assert.Contains("primary-button", button.ClassList);
        Assert.False(button.HasAttribute("disabled"));
        Assert.Empty(document.QuerySelectorAll("a"));
    }

    [Fact]
    public async Task Profile_ClaimedTelegramRequestsExplicitIdentityConfirmationInTheProfile()
    {
        using var api = new FixtureApi(new TelegramLinkDto(true, false, null, null, null, Guid.NewGuid(), "Fixture Telegram", "fixture_account"));
        var document = new HtmlParser().ParseDocument(await Render<TelegramProfilePanel>(api));
        Assert.Contains("Fixture Telegram @fixture_account", document.QuerySelector("strong")!.TextContent);
        Assert.Single(document.QuerySelectorAll("button.primary-button"));
        Assert.Null(document.QuerySelector(".feedback-success"));
    }

    [Theory]
    [InlineData("Administrator")]
    [InlineData("Manager")]
    [InlineData("Customer")]
    public async Task Profile_DoesNotOfferLinkingToOtherRoles(string role)
    {
        using var api = new FixtureApi(new { });
        var document = new HtmlParser().ParseDocument(await Render<TelegramProfilePanel>(api, role));
        Assert.Null(document.QuerySelector(".telegram-profile-panel"));
        Assert.Equal(0, api.Calls);
    }

    private static async Task<string> Render<T>(FixtureApi api, string role = "Seller") where T : IComponent
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLocalization();
        services.AddSingleton(new HttpClient(api, disposeHandler: false) { BaseAddress = new Uri("https://fixture.invalid/") });
        services.AddSingleton<TelegramApiClient>();
        services.AddSingleton<AuthenticationStateProvider>(new FixtureAuthentication(role));
        services.AddSingleton<IJSRuntime, NoJavaScript>();
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var component = await renderer.RenderComponentAsync<T>();
            return WebUtility.HtmlDecode(component.ToHtmlString());
        });
    }

    private sealed class FixtureApi(object payload) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(payload) });
        }
    }

    private sealed class FixtureAuthentication(string role) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(
            new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, role)], "fixture"))));
    }

    private sealed class NoJavaScript : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => throw new InvalidOperationException("Static rendering must not call JavaScript.");
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken ct, object?[]? args) => InvokeAsync<TValue>(identifier, args);
    }
}
