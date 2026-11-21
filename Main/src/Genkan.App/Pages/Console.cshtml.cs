using Genkan.Console;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Genkan.Pages;

[Authorize]
public class ConsoleModel : PageModel
{
    public IReadOnlyList<CommandHints.Hint> Results { get; private set; } = Array.Empty<CommandHints.Hint>();

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    public string? Category { get; private set; }

    public IReadOnlyList<string> Categories { get; } = CommandHints.All
        .Select(hint => hint.Category)
        .Distinct()
        .OrderBy(name => name, StringComparer.Ordinal)
        .ToList();

    public void OnGet(string? category)
    {
        Category = string.IsNullOrWhiteSpace(category) ? null : category;

        Results = CommandHints.Search(Q);
        if (Category is not null)
        {
            Results = Results.Where(hint => string.Equals(hint.Category, Category, StringComparison.OrdinalIgnoreCase)).ToList();
        }
    }
}
