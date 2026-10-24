using System.Text;
using Domain.Entities;

namespace WebApi.Telegram;

// Deliberately exact aliases, not fuzzy title matching: ambiguous categories still need a seller's choice.
public static class ImportCategoryResolver
{
    private static readonly string[][] Aliases =
    [
        ["Ноутбуки", "Ноутбуки та ультрабуки", "Laptops", "Notebooks"],
        ["Смартфони", "Smartphones"],
        ["Планшети", "Tablets"],
        ["Монітори", "Monitors"],
        ["Телевізори", "Televisions"],
        ["Навушники", "Headphones"],
        ["Клавіатури", "Keyboards"],
        ["Миші", "Комп'ютерні миші", "Computer mice"],
        ["Відеокарти", "Graphics cards"],
        ["Процесори", "Processors"],
        ["Материнські плати", "Motherboards"],
        ["Оперативна пам'ять", "RAM"],
        ["Powerbank", "Повербанки", "Павербанки", "Зовнішні акумулятори"],
        ["Пральні машини", "Washing machines"],
        ["Холодильники", "Refrigerators"],
        ["Пилососи", "Vacuum cleaners"],
        ["Сковороди", "Frying pans"],
        ["Каструлі", "Saucepans"],
        ["Ковші", "Milk pans"],
        ["Сотейники", "Сотейники та жаровні"],
        ["Набори посуду для приготування їжі", "Cookware sets"],
        ["Форми для випікання", "Bakeware"],
        ["Дека для випікання", "Baking trays"],
        ["Кришки для посуду", "Cookware lids"],
        ["Еспандери", "Гумові петлі", "Resistance bands", "Expanders"]
    ];

    public static Guid? Resolve(MarketplaceProductData data, IReadOnlyList<Category> categories)
    {
        var name = Normalize(data.CategoryName);
        var exact = categories.Where(c => c.IsActive && Normalize(c.Name) == name).ToList();
        if (exact.Count > 0) return exact.Count == 1 ? exact[0].Id : null;

        // Rozetka's c80003 also contains button phones. Never classify those as smartphones by category alone.
        if (new[] { "Мобільні телефони", "Mobile phones" }.Any(n => Normalize(n) == name))
        {
            var classification = string.Join(' ', data.Characteristics.Where(c =>
                new[] { "Клас", "Тип", "Операційна система", "Operating system" }.Contains(c.Name, StringComparer.OrdinalIgnoreCase)).Select(c => c.Value)).ToLowerInvariant();
            if (classification.Contains("кноп") || classification.Contains("feature phone")) return null;
            if (!(classification.Contains("смартфон") || classification.Contains("smartphone") || classification.Contains("android") || classification.Contains("ios")
                || data.Name.Contains("iPhone", StringComparison.OrdinalIgnoreCase) || data.Name.Contains("смартфон", StringComparison.OrdinalIgnoreCase)
                || data.Name.Contains("smartphone", StringComparison.OrdinalIgnoreCase))) return null;
            name = Normalize("Смартфони");
        }
        var group = Aliases.SingleOrDefault(g => g.Any(alias => Normalize(alias) == name));
        if (group is not null)
        {
            var matches = categories.Where(c => c.IsActive && group.Any(alias => Normalize(alias) == Normalize(c.Name))).ToList();
            if (matches.Count > 0) return matches.Count == 1 ? matches[0].Id : null;
        }
        // Prefer the closest breadcrumb already present in Treba, so a new leaf can be created there.
        foreach (var parentName in data.CategoryPath.Reverse().Where(n => Normalize(n) != name))
        {
            var parents = categories.Where(c => c.IsActive && Normalize(c.Name) == Normalize(parentName)).ToList();
            if (parents.Count > 0) return parents.Count == 1 ? parents[0].Id : null;
        }
        if (new[] { "Еспандери", "Гумові петлі", "Фітнес-резинки", "Гумові петлі для підтягування", "Resistance bands", "Expanders" }.Any(n => Normalize(n) == name))
            foreach (var names in new[] { new[] { "Фітнес", "Fitness", "Тренажери та фітнес" }, new[] { "Спортивні товари" }, new[] { "Спорт і захоплення", "Спорт та захоплення", "Спорт і відпочинок", "Спорт" } })
            {
                var parents = categories.Where(c => c.IsActive && names.Any(n => Normalize(n) == Normalize(c.Name))).ToList();
                if (parents.Count > 0) return parents.Count == 1 ? parents[0].Id : null;
            }
        return null;
    }

    private static string Normalize(string name) => string.Concat(name.Normalize(NormalizationForm.FormKC).ToLowerInvariant().Where(char.IsLetterOrDigit));
}
