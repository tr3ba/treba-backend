using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Application.Abstractions;
using Application.Administration;
using Application.Common;
using Application.Delivery;
using Application.Marketplace;
using Application.Inventory;
using Application.Orders;
using Application.Promotions;
using Contracts.Delivery;
using Domain.Entities;
using Domain.Entities.Users;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.TestHost;
using WebApi.Delivery;
using WebApi.Notifications;
using WebApi.Telegram;

namespace WebApi.Tests;

public sealed class DeliveryAndNotificationTests
{
    internal static readonly Guid CityId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    internal static readonly Guid PointId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private sealed class CarrierHttp(bool wrongCity = false, bool failed = false, bool unfiltered = false) : HttpMessageHandler
    {
        internal readonly List<(string Model, string Method)> Requests = [];
        internal readonly List<JsonElement> Properties = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Equal("https://api.novaposhta.ua/v2.0/json/", request.RequestUri!.AbsoluteUri);
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            var root = json.RootElement; var method = root.GetProperty("calledMethod").GetString()!;
            var properties = root.GetProperty("methodProperties");
            Properties.Add(properties.Clone());
            Assert.All(properties.EnumerateObject(), property => Assert.True(char.IsUpper(property.Name[0]), "Carrier property names must retain their PascalCase spelling."));
            if (method == "getCities")
            {
                Assert.True(properties.TryGetProperty("FindByString", out _) || properties.TryGetProperty("Ref", out _));
                Assert.True(properties.TryGetProperty("Limit", out _));
            }
            if (method == "getWarehouses") Assert.Equal(CityId, properties.GetProperty("CityRef").GetGuid());
            if (method == "getDocumentPrice")
            {
                Assert.Equal(CityId, properties.GetProperty("CitySender").GetGuid());
                Assert.Equal(CityId, properties.GetProperty("CityRecipient").GetGuid());
                Assert.True(properties.TryGetProperty("ServiceType", out _));
            }
            var postomat = !properties.TryGetProperty("CategoryOfWarehouse", out var category) || category.GetString() != "Warehouse";
            Requests.Add((root.GetProperty("modelName").GetString()!, method));
            Assert.DoesNotContain(method, new[] { "save", "update", "delete" });
            var data = method switch
            {
                "getCities" when unfiltered => JsonSerializer.Serialize(new[] {
                    new { Ref = CityId, Description = "Авангард", AreaDescription = "Одеська" },
                    new { Ref = Guid.NewGuid(), Description = "Абазівка", AreaDescription = "Полтавська" },
                    new { Ref = PointId, Description = "Авангард (Дніпропетровська обл.)", AreaDescription = "Дніпропетровська" } }),
                "getCities" => JsonSerializer.Serialize(new[] { new { Ref = CityId, Description = "Київ", AreaDescription = "Київська" } }),
                "getWarehouses" => JsonSerializer.Serialize(new[] { new { Ref = PointId, CityRef = wrongCity ? Guid.NewGuid() : CityId,
                    Description = postomat ? "Поштомат №123" : "Відділення №123", Number = "123", CategoryOfWarehouse = postomat ? "Postomat" : "Branch", WarehouseStatus = "Working", TotalMaxWeightAllowed = "20" } }),
                "getDocumentPrice" => "[{\"Cost\":\"95.50\"}]",
                _ => throw new InvalidOperationException("Unexpected carrier call")
            };
            return new(HttpStatusCode.OK) { Content = new StringContent(failed ? "{\"success\":false,\"errors\":[\"secret upstream detail\"]}" : "{\"success\":true,\"data\":" + data + "}", Encoding.UTF8, "application/json") };
        }
    }

    private static NovaPoshtaClient Carrier(CarrierHttp handler, IMemoryCache cache) => new(new HttpClient(handler), Options.Create(new NovaPoshtaOptions
        { Enabled = true, ApiKey = "fixture-only", SenderCityRef = CityId }), cache);

    [Fact]
    public async Task Carrier_SearchPreservesWireCase_AndCannotReturnUnrelatedAlphabeticalCities()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions()); var handler = new CarrierHttp(unfiltered: true);
        var cities = await Carrier(handler, cache).CitiesAsync("аванг", default);
        Assert.Equal(2, cities.Count);
        Assert.All(cities, city => Assert.StartsWith("Авангард", city.Name));
        Assert.Equal("аванг", Assert.Single(handler.Properties).GetProperty("FindByString").GetString());
        Assert.Equal("20", handler.Properties[0].GetProperty("Limit").GetString());
    }

    [Fact]
    public async Task Carrier_PickupTypesAreFilteredUpstream_AndHaveSeparateCachedPages()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions()); var handler = new CarrierHttp(); var client = Carrier(handler, cache);
        Assert.False(Assert.Single((await client.PickupPointsAsync(CityId, "123", "branch", 1, default)).Items).Postomat);
        Assert.True(Assert.Single((await client.PickupPointsAsync(CityId, "123", "postomat", 1, default)).Items).Postomat);
        await client.PickupPointsAsync(CityId, "123", "branch", 1, default);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal("Warehouse", handler.Properties[0].GetProperty("CategoryOfWarehouse").GetString());
        Assert.Equal("Postomat", handler.Properties[1].GetProperty("CategoryOfWarehouse").GetString());
        Assert.All(handler.Properties, properties => Assert.Equal("123", properties.GetProperty("FindByString").GetString()));
    }

    [Fact]
    public async Task Carrier_KeyOnlyConfiguration_EnablesDirectoryAndResolvesSenderCity()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDeliveryNotifications(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["NovaPoshta:ApiKey"] = " fixture-only ",
            ["Delivery:NovaPoshta:ApiKey"] = ""
        }).Build());
        using var container = services.BuildServiceProvider();
        var options = container.GetRequiredService<IOptions<NovaPoshtaOptions>>();
        Assert.True(options.Value.Configured); Assert.Equal("fixture-only", options.Value.ApiKey);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var handler = new CarrierHttp();
        var carrier = new NovaPoshtaClient(new HttpClient(handler), options, cache);
        Assert.Single(await carrier.CitiesAsync("Київ", default));
        Assert.Equal(95.50m, (await carrier.QuoteAsync(CityId, PointId, 1, 200, default)).Price);
        Assert.DoesNotContain(handler.Requests, r => r.Method is "save" or "update");
    }

    [Fact]
    public void Carrier_ExplicitDisableTakesPrecedenceOverApiKey()
    {
        var services = new ServiceCollection(); services.AddLogging();
        services.AddDeliveryNotifications(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["NovaPoshta:ApiKey"] = "fixture-only", ["Delivery:NovaPoshta:Enabled"] = "false"
        }).Build());
        using var container = services.BuildServiceProvider();
        Assert.False(container.GetRequiredService<IOptions<NovaPoshtaOptions>>().Value.Configured);
    }

    [Fact]
    public async Task Carrier_UsesReadOnlyMethods_CachesDirectory_AndCalculatesPostomatPrice()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions()); var handler = new CarrierHttp(); var client = Carrier(handler, cache);
        Assert.Single(await client.CitiesAsync("Київ", default)); Assert.Single(await client.CitiesAsync("Київ", default));
        Assert.Single(handler.Requests);
        var result = await client.QuoteAsync(CityId, PointId, 1, 250, default);
        Assert.Equal(95.50m, result.Price); Assert.True(result.PickupPoint.Postomat);
        Assert.Equal("getDocumentPrice", handler.Requests.Last().Method);
    }

    [Fact]
    public async Task Carrier_RejectsWrongCityAndOverweight_BeforePriceRequest()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var handler = new CarrierHttp(wrongCity: true);
        await Assert.ThrowsAsync<ArgumentException>(() => Carrier(handler, cache).QuoteAsync(CityId, PointId, 1, 250, default));
        Assert.DoesNotContain(handler.Requests, r => r.Method == "getDocumentPrice");
        var overweight = new CarrierHttp();
        await Assert.ThrowsAsync<ArgumentException>(() => Carrier(overweight, cache).QuoteAsync(CityId, PointId, 21, 250, default));
        Assert.DoesNotContain(overweight.Requests, r => r.Method == "getDocumentPrice");
    }

    [Fact]
    public async Task Carrier_UnsuccessfulEnvelope_DoesNotLeakProviderDetails()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var error = await Assert.ThrowsAsync<DeliveryUnavailableException>(() => Carrier(new(failed: true), cache).CitiesAsync("Київ", default));
        Assert.DoesNotContain("secret", error.Message);
    }

    internal sealed class Provider : ICheckoutDeliveryProvider
    {
        public bool Available => true;
        public decimal DefaultWeightKg => 1;
        public Task<IReadOnlyList<DeliveryCity>> CitiesAsync(string query, CancellationToken ct) => Task.FromResult<IReadOnlyList<DeliveryCity>>([new(CityId, "Київ", "Київська")]);
        public Task<DeliveryPickupPage> PickupPointsAsync(Guid cityRef, string query, string kind, int page, CancellationToken ct) => Task.FromResult(new DeliveryPickupPage([new(PointId, CityId, "Поштомат №123", "123", true)], page, false));
        public Task<(DeliveryCity City, DeliveryPickupPoint PickupPoint, decimal Price)> QuoteAsync(Guid cityRef, Guid pickupPointRef, decimal weightKg, decimal declaredValue, CancellationToken ct) =>
            Task.FromResult<(DeliveryCity, DeliveryPickupPoint, decimal)>((new(CityId, "Київ", "Київська"), new(PointId, CityId, "Поштомат №123", "123", true), 95.50m));
    }

    internal static CheckoutDeliveryService Delivery(ApplicationDbContext db, OperationsTests.Actor actor, TimeProvider clock) => new(db, new Provider(), new DiscountService(db), actor, clock);
    internal static OrderService Orders(ApplicationDbContext db, OperationsTests.Actor actor, CheckoutDeliveryService delivery) =>
        new(db, new InventoryService(db, actor), new DiscountService(db), actor, delivery, new CustomerNotificationQueue(db));

    [Fact]
    public async Task Checkout_BindsQuoteToCustomerCartAndPrice_UsesCanonicalAddress_AndCannotReuseQuote()
    {
        await using var db = TelegramTestData.Memory(); var seed = await OperationsTests.SeedAsync(db);
        var clock = new TelegramTestData.Clock(); var actor = new OperationsTests.Actor(seed.Customer.Id);
        var delivery = Delivery(db, actor, clock); var items = new List<DeliveryQuoteItem> { new(seed.Variant.Id, 2) };
        var quote = await delivery.QuoteAsync(new(CityId, PointId, items), default);
        Assert.True(quote.EstimatedWeight);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => delivery.ValidateAsync(quote.Id, seed.Seller.User.Id, items, null, 200, 200, default));
        await Assert.ThrowsAsync<BusinessRuleException>(() => delivery.ValidateAsync(quote.Id, seed.Customer.Id, [new(seed.Variant.Id, 3)], null, 200, 200, default));
        await Assert.ThrowsAsync<BusinessRuleException>(() => delivery.ValidateAsync(quote.Id, seed.Customer.Id, items, null, 201, 201, default));
        var orders = Orders(db, actor, delivery);
        var result = await orders.CreateOrderAsync(OperationsTests.Request(seed) with { DeliveryQuoteId = quote.Id });
        Assert.Equal(95.50m, result.DeliveryPrice); Assert.Equal(295.50m, result.TotalAmount);
        var shipment = Assert.Single(await db.OrderShipments.ToListAsync()); Assert.Null(shipment.Reference);
        Assert.Equal("NovaPoshta", shipment.Carrier);
        Assert.Equal("Київ", (await db.OrderAddresses.SingleAsync()).City);
        Assert.Equal("Поштомат №123", (await db.OrderAddresses.SingleAsync()).Street);
        var view = (await orders.DetailsAsync(result.Id, default))!.Delivery!;
        Assert.Equal("NovaPoshta", view.Carrier); Assert.Null(view.Reference); Assert.Empty(view.Notice);
        var account = new CustomerAccountService(db, actor, delivery);
        Assert.Empty((await account.ShipmentsAsync(1, null, default)).Content);
        var manager = new OperationsTests.Actor(seed.Seller.User.Id, "Manager");
        var fulfillment = Orders(db, manager, Delivery(db, manager, clock));
        var version = (await fulfillment.DetailsAsync(result.Id, default))!.Version;
        await fulfillment.ChangeStatusAsync(result.Id, new("PAID", version, "Received"), default);
        Assert.StartsWith("TRB-", shipment.Reference);
        Assert.Equal(Domain.Enums.PaymentStatus.SUCCEEDED, (await db.Orders.SingleAsync()).PaymentStatus);
        var reference = shipment.Reference;
        await Assert.ThrowsAsync<BusinessRuleException>(() => fulfillment.ChangeStatusAsync(result.Id, new("PAID", version, "Repeated"), default));
        foreach (var status in new[] { "CONFIRMED", "PROCESSING", "READY_FOR_SHIPMENT", "SHIPPED", "DELIVERED" })
        {
            var current = (await fulfillment.DetailsAsync(result.Id, default))!;
            await fulfillment.ChangeStatusAsync(result.Id, new(status, current.Version, null), default);
            Assert.Equal(reference, (await fulfillment.DetailsAsync(result.Id, default))!.Delivery!.Reference);
            Assert.Equal(status, (await account.OrderAsync(result.Id, default))!.Delivery!.Status);
        }
        Assert.Equal(result.Id, Assert.Single((await account.ShipmentsAsync(1, shipment.Reference, default)).Content).Id);
        Assert.Empty((await new CustomerAccountService(db, new OperationsTests.Actor(seed.Seller.User.Id), delivery).ShipmentsAsync(1, shipment.Reference, default)).Content);
        await Assert.ThrowsAsync<BusinessRuleException>(() => orders.CreateOrderAsync(OperationsTests.Request(seed) with { DeliveryQuoteId = quote.Id }));
        Assert.Equal(8, seed.Variant.StockQuantity);
    }

    [Fact]
    public async Task Courier_StoresDestination_ActivatesAfterPayment_AndIncludesReferenceInNotification()
    {
        await using var db = TelegramTestData.Memory(); var seed = await OperationsTests.SeedAsync(db);
        db.CustomerNotificationSettings.Add(new() { UserId = seed.Customer.Id, OrdersEmail = true }); await db.SaveChangesAsync();
        var clock = new TelegramTestData.Clock(); var customer = new OperationsTests.Actor(seed.Customer.Id);
        var request = OperationsTests.Request(seed) with { DeliveryMethod = "TrebaCourier" };
        var order = await Orders(db, customer, Delivery(db, customer, clock)).CreateOrderAsync(request);
        var shipment = await db.OrderShipments.SingleAsync();
        Assert.Equal("TrebaCourier", shipment.Carrier); Assert.Null(shipment.Reference);
        Assert.Equal(request.Address.City, shipment.City); Assert.Contains(request.Address.Street, shipment.PickupPoint);
        Assert.Equal(50, order.DeliveryPrice);
        Assert.DoesNotContain("Номер відправлення", (await db.CustomerNotifications.SingleAsync()).Text);
        var manager = new OperationsTests.Actor(seed.Seller.User.Id, "Administrator");
        var fulfillment = Orders(db, manager, Delivery(db, manager, clock));
        await fulfillment.PayOrderAsync(order.Id);
        Assert.StartsWith("TRB-", shipment.Reference);
        var paid = await db.CustomerNotifications.SingleAsync(n => n.Title == "Оплату замовлення підтверджено");
        Assert.Contains(shipment.Reference!, paid.Text); Assert.Contains("Кур’єр TREBA", paid.Text);
    }

    [Theory]
    [InlineData("NovaPoshta")]
    [InlineData("unknown")]
    public async Task Checkout_RejectsMissingCarrierQuoteOrUnknownMethod(string carrier)
    {
        await using var db = TelegramTestData.Memory(); var seed = await OperationsTests.SeedAsync(db);
        var actor = new OperationsTests.Actor(seed.Customer.Id); var clock = new TelegramTestData.Clock();
        await Assert.ThrowsAsync<ArgumentException>(() => Orders(db, actor, Delivery(db, actor, clock))
            .CreateOrderAsync(OperationsTests.Request(seed) with { DeliveryMethod = carrier }));
        Assert.Empty(db.Orders); Assert.Equal(10, seed.Variant.StockQuantity);
    }

    [Fact]
    public async Task CancelBeforePayment_DoesNotIssueShipmentReference()
    {
        await using var db = TelegramTestData.Memory(); var seed = await OperationsTests.SeedAsync(db);
        var actor = new OperationsTests.Actor(seed.Customer.Id); var clock = new TelegramTestData.Clock();
        var orders = Orders(db, actor, Delivery(db, actor, clock));
        var order = await orders.CreateOrderAsync(OperationsTests.Request(seed));
        await orders.CancelOrderAsync(order.Id, seed.Customer.Id, "Changed plans");
        Assert.Null((await db.OrderShipments.SingleAsync()).Reference);
        Assert.Empty((await new CustomerAccountService(db, actor, Delivery(db, actor, clock)).ShipmentsAsync(1, null, default)).Content);
        Assert.Equal(10, seed.Variant.StockQuantity);
    }

    [Fact]
    public async Task Checkout_ExpiredQuote_DoesNotReserveStockOrQueueNotification()
    {
        await using var db = TelegramTestData.Memory(); var seed = await OperationsTests.SeedAsync(db);
        var clock = new TelegramTestData.Clock(); var actor = new OperationsTests.Actor(seed.Customer.Id);
        var delivery = Delivery(db, actor, clock);
        var quote = await delivery.QuoteAsync(new(CityId, PointId, [new(seed.Variant.Id, 2)]), default);
        clock.Now = clock.Now.AddMinutes(11);
        await Assert.ThrowsAsync<BusinessRuleException>(() => Orders(db, actor, delivery).CreateOrderAsync(OperationsTests.Request(seed) with { DeliveryQuoteId = quote.Id }));
        Assert.Equal(10, seed.Variant.StockQuantity); Assert.Empty(db.Orders); Assert.Empty(db.CustomerNotifications);
    }

    [Fact]
    public async Task Checkout_ChangedProductWeightInvalidatesQuote()
    {
        await using var db = TelegramTestData.Memory(); var seed = await OperationsTests.SeedAsync(db);
        var clock = new TelegramTestData.Clock(); var actor = new OperationsTests.Actor(seed.Customer.Id); var delivery = Delivery(db, actor, clock);
        var quote = await delivery.QuoteAsync(new(CityId, PointId, [new(seed.Variant.Id, 2)]), default);
        var v = seed.Variant; v.Update(v.Sku, v.Barcode, v.Name, v.Price, v.OldPrice, v.CostPrice, 2, v.Length, v.Width, v.Height, v.IsActive);
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<BusinessRuleException>(() => Orders(db, actor, delivery).CreateOrderAsync(OperationsTests.Request(seed) with { DeliveryQuoteId = quote.Id }));
        Assert.Empty(db.Orders); Assert.Equal(10, v.StockQuantity);
    }

    internal sealed class Channels : INotificationChannels { public bool EmailAvailable => true; public bool TelegramAvailable => true; }
    internal sealed class Bot : ITelegramBotClient
    {
        internal readonly List<string> Sent = [];
        public Task<JsonDocument> CallAsync(string method, object payload, CancellationToken ct)
        { Sent.Add(JsonSerializer.Serialize(payload)); return Task.FromResult(JsonDocument.Parse("{}")); }
        public Task SendMenuAsync(long chatId, string text, CancellationToken ct) => throw new NotSupportedException();
        public Task SendTextAsync(long chatId, string text, CancellationToken ct) => throw new NotSupportedException();
    }
    internal sealed class Mail : ITransactionalEmailSender
    {
        internal bool Fail; internal readonly List<(string Email, string Text)> Sent = [];
        public Task SendNotificationAsync(string recipient, string title, string text, string url, CancellationToken ct)
        { if (Fail) throw new HttpRequestException("fixture failure"); Sent.Add((recipient, text)); return Task.CompletedTask; }
    }
    internal static CustomerNotificationRunner Runner(ApplicationDbContext db, Mail mail, TelegramTestData.Clock clock) =>
        new(db, mail, new Bot(), new Channels(), Options.Create(new CustomerNotificationOptions { Enabled = true, StoreOrigin = "https://treba.example" }), clock);

    [Fact]
    public async Task Notification_RetriesPersistently_UsesVerifiedAccountEmail_AndHonorsUnsubscribe()
    {
        await using var db = TelegramTestData.Memory(); var seed = await OperationsTests.SeedAsync(db);
        db.CustomerNotificationSettings.Add(new() { UserId = seed.Customer.Id, OrdersEmail = true }); await db.SaveChangesAsync();
        var clock = new TelegramTestData.Clock(); var actor = new OperationsTests.Actor(seed.Customer.Id); var delivery = Delivery(db, actor, clock);
        var order = await Orders(db, actor, delivery).CreateOrderAsync(OperationsTests.Request(seed));
        var item = Assert.Single(db.CustomerNotifications); Assert.Empty(new Mail().Sent);
        clock.Now = DateTimeOffset.UtcNow.AddSeconds(1);
        var mail = new Mail { Fail = true }; var runner = Runner(db, mail, clock);
        Assert.True(await runner.DeliverOneAsync(default)); Assert.Null(item.CompletedAt); Assert.Equal(1, item.Attempts);
        Assert.False(await runner.DeliverOneAsync(default)); clock.Now = clock.Now.AddMinutes(2); mail.Fail = false;
        Assert.True(await runner.DeliverOneAsync(default)); Assert.Equal(seed.Customer.Email, Assert.Single(mail.Sent).Email);
        Assert.NotEqual("fixture@example.test", mail.Sent[0].Email); Assert.NotNull(item.CompletedAt);
        Assert.False(await runner.DeliverOneAsync(default));
        var staff = new OperationsTests.Actor(seed.Seller.User.Id, "Administrator");
        var staffOrders = Orders(db, staff, Delivery(db, staff, clock));
        var details = (await staffOrders.DetailsAsync(order.Id, default))!;
        await staffOrders.ChangeStatusAsync(order.Id, new("PAID", details.Version, "private payment note"), default);
        var preferences = await db.CustomerNotificationSettings.SingleAsync(); preferences.OrdersEmail = false; await db.SaveChangesAsync();
        clock.Now = clock.Now.AddMinutes(1);
        Assert.True(await runner.DeliverOneAsync(default)); Assert.Single(mail.Sent);
    }

    [Fact]
    public async Task Support_ExcludesInternalNotes_AndDeletesPreventQueuedMessagesFromBeingSent()
    {
        await using var db = TelegramTestData.Memory(); var seed = await OperationsTests.SeedAsync(db);
        db.CustomerNotificationSettings.Add(new() { UserId = seed.Customer.Id, SupportEmail = true }); await db.SaveChangesAsync();
        var clock = new TelegramTestData.Clock(); var customer = new OperationsTests.Actor(seed.Customer.Id);
        var queue = new CustomerNotificationQueue(db);
        var customerSupport = new SupportService(db, customer, Orders(db, customer, Delivery(db, customer, clock)), queue);
        var ticket = await customerSupport.CreateAsync(new() { Kind = "CHAT", Subject = "Fixture", Message = "Customer private text" }, default);
        var staff = new OperationsTests.Actor(seed.Seller.User.Id, "Manager");
        var support = new SupportService(db, staff, Orders(db, staff, Delivery(db, staff, clock)), queue);
        await support.MessageAsync(ticket.Summary.Id, new("Internal secret", true), default);
        Assert.Empty(db.CustomerNotifications);
        var messageId = Guid.NewGuid(); await support.MessageAsync(ticket.Summary.Id, new("Public secret", false, messageId), default);
        await support.MessageAsync(ticket.Summary.Id, new("Public secret", false, messageId), default);
        var notification = Assert.Single(db.CustomerNotifications); Assert.DoesNotContain("secret", notification.Text);
        (await db.SupportTickets.SingleAsync()).IsDeleted = true; await db.SaveChangesAsync();
        clock.Now = clock.Now.AddMinutes(1); var mail = new Mail();
        Assert.True(await Runner(db, mail, clock).DeliverOneAsync(default)); Assert.Empty(mail.Sent); Assert.NotNull(notification.CompletedAt);
    }

    [Fact]
    public async Task Customer_CanLinkTelegramForNotifications_ButCannotUseStaffGuard()
    {
        await using var db = TelegramTestData.Memory(); var user = await TelegramTestData.UserAsync(db, "Customer");
        var clock = new TelegramTestData.Clock(); var links = new TelegramLinkService(db, TelegramTestData.Options, clock);
        var start = await links.BeginAsync(user.Id, default);
        await links.ClaimAsync(start.BotUrl.Split("link_")[1], 555, 555, "customer_fixture", "Customer", default);
        Assert.False((await links.StatusAsync(user.Id, default)).Linked);
        Assert.True((await links.ConfirmAsync(user.Id, start.Id, default)).Linked);
        Assert.False(TelegramLinkService.EligibleRole("Customer"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => links.RequireUserAsync(user.Id, default));
    }

    [Fact]
    public async Task TelegramNotification_UsesConfirmedPrivateChat_AndStopsAfterUnlinking()
    {
        await using var db = TelegramTestData.Memory(); var seed = await OperationsTests.SeedAsync(db); var clock = new TelegramTestData.Clock();
        await TelegramTestData.BindAsync(db, seed.Customer, clock, 777);
        db.CustomerNotificationSettings.Add(new() { UserId = seed.Customer.Id, OrdersTelegram = true }); await db.SaveChangesAsync();
        var actor = new OperationsTests.Actor(seed.Customer.Id); var order = await Orders(db, actor, Delivery(db, actor, clock)).CreateOrderAsync(OperationsTests.Request(seed));
        clock.Now = DateTimeOffset.UtcNow.AddSeconds(1); var bot = new Bot();
        var runner = new CustomerNotificationRunner(db, new Mail(), bot, new Channels(), Options.Create(new CustomerNotificationOptions { Enabled = true, StoreOrigin = "https://treba.example" }), clock);
        Assert.True(await runner.DeliverOneAsync(default));
        using (var payload = JsonDocument.Parse(Assert.Single(bot.Sent))) Assert.Equal(777, payload.RootElement.GetProperty("chat_id").GetInt64());
        var staff = new OperationsTests.Actor(seed.Seller.User.Id, "Manager"); var staffOrders = Orders(db, staff, Delivery(db, staff, clock));
        await staffOrders.ChangeStatusAsync(order.Id, new("PAID", (await staffOrders.DetailsAsync(order.Id, default))!.Version, "private note"), default);
        await new TelegramLinkService(db, TelegramTestData.Options, clock).UnlinkAsync(seed.Customer.Id, default);
        clock.Now = clock.Now.AddSeconds(1); Assert.True(await runner.DeliverOneAsync(default)); Assert.Single(bot.Sent);
        Assert.DoesNotContain("private note", bot.Sent[0]);
    }

    [Fact]
    public async Task DeliveryAndSettingsHttp_RequireAuthentication_ProtectQuotesAndRejectStalePreferences()
    {
        await using var server = await AdminHttpTests.ServerAsync(services =>
        {
            services.AddDeliveryNotifications(new ConfigurationBuilder().Build());
            services.AddSingleton(TimeProvider.System); services.AddScoped<ICheckoutDeliveryProvider, Provider>();
        });
        using var client = server.GetTestClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/checkout-delivery/capabilities")).StatusCode);
        OperationsTests.Seed seed;
        using (var scope = server.Services.CreateScope()) seed = await OperationsTests.SeedAsync(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
        client.DefaultRequestHeaders.Add("X-Test-Role", "Customer"); client.DefaultRequestHeaders.Add("X-Test-UserId", seed.Customer.Id.ToString());
        Assert.True((await client.GetFromJsonAsync<DeliveryCapabilities>("/api/v1/checkout-delivery/capabilities"))!.Available);
        var request = new DeliveryQuoteRequest(CityId, PointId, [new(seed.Variant.Id, 2)]);
        var quoted = await client.PostAsJsonAsync("/api/v1/checkout-delivery/quotes", request);
        Assert.Equal(HttpStatusCode.OK, quoted.StatusCode);
        var quote = (await quoted.Content.ReadFromJsonAsync<DeliveryQuoteView>())!;
        var order = await client.PostAsJsonAsync("/api/v1/orders", OperationsTests.Request(seed) with { DeliveryQuoteId = quote.Id });
        Assert.Equal(HttpStatusCode.Created, order.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/account/shipments")).StatusCode);
        var settings = (await client.GetFromJsonAsync<Contracts.Marketplace.NotificationSettingsView>("/api/v1/account/notification-settings"))!;
        var save = new Contracts.Marketplace.SaveNotificationSettingsRequest(true, false, true, false, settings.Version);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync("/api/v1/account/notification-settings", save)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync("/api/v1/account/notification-settings", save)).StatusCode);
        client.DefaultRequestHeaders.Remove("X-Test-UserId"); client.DefaultRequestHeaders.Add("X-Test-UserId", seed.Seller.User.Id.ToString());
        Assert.False((await client.GetFromJsonAsync<Contracts.Marketplace.NotificationSettingsView>("/api/v1/account/notification-settings"))!.OrdersEmail);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync("/api/v1/orders", OperationsTests.Request(seed) with { CustomerId = Guid.Empty, DeliveryQuoteId = quote.Id })).StatusCode);
    }
}
