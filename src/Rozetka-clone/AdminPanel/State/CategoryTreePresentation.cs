using AdminPanel.Services.Abstractions;

namespace AdminPanel.State;

public static class CategoryTreePresentation
{
    public sealed record Row(CategoryTreeDto Category, string Path, int Depth);
    public static IReadOnlyList<Row> Flatten(IEnumerable<CategoryTreeDto> roots)
    {
        var result = new List<Row>(); var seen = new HashSet<Guid>();
        void Visit(IEnumerable<CategoryTreeDto> nodes, string prefix, int depth)
        {
            foreach (var node in nodes)
            {
                if (!seen.Add(node.Id)) continue;
                var path = prefix.Length == 0 ? node.Name : prefix + " → " + node.Name;
                result.Add(new(node, path, depth)); Visit(node.Children, path, depth + 1);
            }
        }
        Visit(roots, "", 0); return result;
    }
    public static IReadOnlyList<Row> Visible(IEnumerable<CategoryTreeDto> roots, ISet<Guid> expanded, string? search)
    {
        var all = Flatten(roots);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var matches = all.Where(row => row.Path.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase)
                || row.Category.Slug.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
            var included = matches.Select(row => row.Category.Id).ToHashSet();
            // Keep ancestors so a search result retains its place in the tree.
            foreach (var row in all.Where(row => row.Category.Children.Count > 0))
                if (Flatten(row.Category.Children).Any(child => included.Contains(child.Category.Id))) included.Add(row.Category.Id);
            return all.Where(row => included.Contains(row.Category.Id)).ToList();
        }
        var result = new List<Row>(); var hiddenBelow = int.MaxValue;
        foreach (var row in all)
        {
            if (row.Depth > hiddenBelow) continue;
            result.Add(row); hiddenBelow = expanded.Contains(row.Category.Id) ? int.MaxValue : row.Depth;
        }
        return result;
    }
}
