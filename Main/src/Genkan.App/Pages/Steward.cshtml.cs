using Genkan.Ai;
using Genkan.Backup;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Genkan.Pages;

[Authorize(Roles = "admin")]
public class StewardModel : PageModel
{
    private readonly AiService _ai;
    private readonly SecretStore _secrets;
    private readonly ChatStore _chats;

    public StewardModel(AiService ai, SecretStore secrets, ChatStore chats)
    {
        _ai = ai;
        _secrets = secrets;
        _chats = chats;
    }

    public IReadOnlyList<AiProvider> Providers { get; private set; } = Array.Empty<AiProvider>();

    public string DefaultProvider { get; private set; } = string.Empty;

    public bool TotpBound => _secrets.Get("totp.enabled") == "1";

    public IReadOnlyList<ChatMessage> History { get; private set; } = Array.Empty<ChatMessage>();

    public void OnGet()
    {
        Providers = _ai.Catalog.Providers.Where(provider => _ai.HasKey(provider.Id)).ToList();
        DefaultProvider = _ai.Catalog.DefaultProvider;

        var owner = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "unknown";
        var document = _chats.Load(owner, "steward-main");
        History = document?.Messages ?? new List<ChatMessage>();
    }
}
