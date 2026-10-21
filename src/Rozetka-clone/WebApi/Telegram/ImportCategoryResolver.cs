using System.Text;
using Domain.Entities;

namespace WebApi.Telegram;

// Deliberately exact aliases, not fuzzy title matching: ambiguous categories still need a seller's choice.
public static class ImportCategoryResolver
{
    private static readonly string[][] Aliases =
    [
        ["Ноутбуки", "Ноутбуки та ультрабуки", "Laptops", "Notebooks"],
        ["Смартфони", "Смартфоны", "Smartphones"],
        ["Планшети", "Планшеты", "Tablets"],
        ["Монітори", "Мониторы", "Monitors"],
        ["Телевізори", "Телевизоры", "Televisions"],
        ["Навушники", "Наушники", "Headphones"],
        ["Клавіатури", "Клавиатуры", "Keyboards"],
        ["Миші", "Мыши", "Комп'ютерні миші", "Компьютерные мыши"],
        ["Відеокарти", "Видеокарты", "Graphics cards"],
        ["Процесори", "Процессоры", "Processors"],
        ["Материнські плати", "Материнские платы", "Motherboards"],
        ["Оперативна пам'ять", "Оперативная память", "RAM"],
        ["Powerbank", "Повербанки", "Павербанки", "Зовнішні акумулятори", "Внешние аккумуляторы"],
        ["Пральні машини", "Стиральные машины", "Washing machines"],
        ["Холодильники", "Refrigerators"],
        ["Пилососи", "Пылесосы", "Vacuum cleaners"],
        ["Сковороди", "Сковородки", "Сковороды", "Frying pans"],
        ["Каструлі", "Кастрюли", "Saucepans"],
        ["Ковші", "Ковши"],
        ["Сотейники", "Сотейники та жаровні"],
        ["Набори посуду для приготування їжі", "Наборы кухонной посуды", "Наборы посуды для приготовления пищи"],
        ["Форми для випікання", "Формы для выпечки", "Bakeware"],
        ["Дека для випікання", "Противни"],
        ["Кришки для посуду", "Крышки для посуды"]
    ];

    public static Guid? Resolve(MarketplaceProductData data, IReadOnlyList<Category> categories)
    {
        var name = Normalize(data.CategoryName);
        var exact = categories.Where(c => c.IsActive && Normalize(c.Name) == name).ToList();
        if (exact.Count > 0) return exact.Count == 1 ? exact[0].Id : null;

        // Rozetka's c80003 also contains button phones. Never classify those as smartphones by category alone.
        if (new[] { "Мобільні телефони", "Мобильные телефоны", "Mobile phones" }.Any(n => Normalize(n) == name))
        {
            var classification = string.Join(' ', data.Characteristics.Where(c =>
                new[] { "Клас", "Класс", "Тип", "Операційна система", "Операционная система", "Operating system" }.Contains(c.Name, StringComparer.OrdinalIgnoreCase)).Select(c => c.Value)).ToLowerInvariant();
            if (classification.Contains("кноп") || classification.Contains("feature phone")) return null;
            if (!(classification.Contains("смартфон") || classification.Contains("smartphone") || classification.Contains("android") || classification.Contains("ios")
                || data.Name.Contains("iPhone", StringComparison.OrdinalIgnoreCase) || data.Name.Contains("смартфон", StringComparison.OrdinalIgnoreCase)
                || data.Name.Contains("smartphone", StringComparison.OrdinalIgnoreCase))) return null;
            name = Normalize("Смартфони");
        }
        var group = Aliases.SingleOrDefault(g => g.Any(alias => Normalize(alias) == name));
        if (group is null) return null;
        var matches = categories.Where(c => c.IsActive && group.Any(alias => Normalize(alias) == Normalize(c.Name))).ToList();
        return matches.Count == 1 ? matches[0].Id : null;
    }

    private static string Normalize(string name) => string.Concat(name.Normalize(NormalizationForm.FormKC).ToLowerInvariant().Where(char.IsLetterOrDigit));
}
