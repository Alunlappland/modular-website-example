using System.Security.Claims;
using Genkan.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Genkan.Pages;

[Authorize]
public class MeModel : PageModel
{
    private readonly UserRepository _users;

    public MeModel(UserRepository users) => _users = users;

    public UserRecord? Me { get; private set; }

    public string CreatedText { get; private set; } = "—";

    public string LastLoginText { get; private set; } = "—";

    public void OnGet()
    {
        var id = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        Me = string.IsNullOrWhiteSpace(id) ? null : _users.FindById(id);

        if (Me is null)
        {
            return;
        }

        CreatedText = Format(Me.CreatedAt);
        LastLoginText = Format(Me.LastLoginAt);
    }

    private static string Format(string? iso)
    {
        if (string.IsNullOrWhiteSpace(iso) || !DateTimeOffset.TryParse(iso, out var value))
        {
            return "—";
        }

        return value.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
    }
}
