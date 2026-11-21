using Genkan.Ai;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Genkan.Pages;

[Authorize]
public class AiModel : PageModel
{
    private readonly AiService _ai;

    public AiModel(AiService ai) => _ai = ai;

    public IReadOnlyList<AiProvider> Providers { get; private set; } = Array.Empty<AiProvider>();

    public string DefaultProvider { get; private set; } = string.Empty;

    public IReadOnlyList<AiProvider> MissingKeys { get; private set; } = Array.Empty<AiProvider>();

    public void OnGet()
    {
        Providers = _ai.Catalog.Providers;
        DefaultProvider = _ai.Catalog.DefaultProvider;
        MissingKeys = Providers.Where(provider => !_ai.HasKey(provider.Id)).ToList();
    }
}
