using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Genkan.Pages;

public class IndexModel : PageModel
{
    public bool Authed { get; private set; }
    public string Display { get; private set; } = "访客";

    public void OnGet()
    {
        Authed = User.Identity?.IsAuthenticated == true;
        Display = User.FindFirst("display")?.Value
                  ?? User.Identity?.Name
                  ?? "访客";
    }
}
