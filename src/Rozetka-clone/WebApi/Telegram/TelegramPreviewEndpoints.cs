using Contracts.Catalog;
using Contracts.Telegram;

namespace WebApi.Telegram;

public static class TelegramPreviewEndpoints
{
    public static void Map(WebApplication app)
    {
        if (!app.Environment.IsDevelopment()) return;
        app.MapGet("/api/telegram/preview", (string? role) =>
        {
            role = role == "Moderator" ? "Moderator" : "Seller";
            var category = Guid.Parse("10000000-0000-0000-0000-000000000001");
            var brand = Guid.Parse("10000000-0000-0000-0000-000000000002");
            var store = Guid.Parse("10000000-0000-0000-0000-000000000003");
            var user = Guid.Parse("10000000-0000-0000-0000-000000000004");
            var now = DateTime.UtcNow;
            CatalogProductDto Product(string suffix, string name, string sku, string status, decimal price, int stock) =>
                new(Guid.Parse("20000000-0000-0000-0000-00000000000" + suffix), store, category, brand, null, name, "demo-" + suffix,
                    "Зволожувачі повітря", "LEVOIT", status, "Умный увлажнитель для повседневного комфорта.",
                    "Компактный увлажнитель для дома. Удобное управление, простой уход и спокойный повседневный дизайн.", sku, price, null, stock, 24, "Китай", now.AddDays(-1), [])
                { StoreName = "Treba Studio", SellerName = "Treba Studio", UpdatedAt = now, Variants = [new() { Id = Guid.NewGuid(), Name = "Основной", Sku = sku, Price = price, StockQuantity = stock }] };
            var products = new[] {
                Product("1", "Зволожувач повітря LEVOIT Dual 200S Smart", "TRB-DEMO-001", role == "Moderator" ? "PENDING_MODERATION" : "ACTIVE", 2499, 25),
                Product("2", "Компактний зволожувач для дому", "TRB-DEMO-002", role == "Moderator" ? "PENDING_MODERATION" : "DRAFT", 1799, 12),
                Product("3", "Ультразвуковий зволожувач повітря", "TRB-DEMO-003", "REJECTED", 2199, 8)
            };
            return Results.Ok(new {
                bootstrap = new { user = new TelegramUserDto(user, "Саша Treba", role, 9990001), stats = new CatalogStatsDto(17, 12, 2, 2, 1, 0),
                    categories = new[] { new CatalogLookupDto(category,"Побутова техніка → Клімат-техніка → Зволожувачі повітря") },
                    brands = new[] { new CatalogLookupDto(brand,"LEVOIT") }, stores = role == "Seller" ? new[] { new CatalogLookupDto(store,"Treba Studio") } : [], allowedCategoryIds = new[] { category }, pendingReviews = 3 },
                products,
                importPreview = new ImportPreviewDto(Guid.Parse("30000000-0000-0000-0000-000000000001"), "https://rozetka.com.ua/demo/p123456/",
                    "Зволожувач LEVOIT Dual 200S Smart", "Пример описания импортируемого товара. Проверьте комплектацию и характеристики перед отправкой.", "LEVOIT",
                    "Зволожувачі повітря", category, "Побутова техніка → Клімат-техніка → Зволожувачі повітря", 2499, [],
                    [new ImportedCharacteristicDto("Объём резервуара", "3 л"), new ImportedCharacteristicDto("Площадь помещения", "27 м²")], DateTimeOffset.UtcNow.AddHours(1), null, null),
                reviews = new[] { new { id = Guid.NewGuid(), productName = "Зволожувач LEVOIT Dual 200S", authorName = "Покупатель Treba", rating = 5, title = "Удобный товар для дома", comment = "Понятное управление, приятный дизайн. Всё понравилось.", createdAt = now } },
                history = new[] { new ProductModerationDto(Guid.NewGuid(), DateTimeOffset.UtcNow.AddHours(-2), "Модератор Treba", "Rejected", "Добавьте подробное описание комплектации и проверьте характеристики.") }
            });
        }).AllowAnonymous().WithMetadata(new Microsoft.AspNetCore.Mvc.ResponseCacheAttribute { NoStore = true, Location = Microsoft.AspNetCore.Mvc.ResponseCacheLocation.None });
    }
}
