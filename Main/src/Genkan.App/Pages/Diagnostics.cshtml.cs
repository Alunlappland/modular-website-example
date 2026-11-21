using Genkan.Sys;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Genkan.Pages;

[Authorize(Roles = "admin")]
public class DiagnosticsModel : PageModel
{
    private readonly DiagnosticsService _diagnostics;

    public DiagnosticsModel(DiagnosticsService diagnostics) => _diagnostics = diagnostics;

    public IReadOnlyList<DiagnosticCheck> Checks { get; private set; } = Array.Empty<DiagnosticCheck>();

    public string CheckedAt { get; private set; } = string.Empty;

    public int FailCount => Checks.Count(check => check.Status == "fail");

    public int WarnCount => Checks.Count(check => check.Status == "warn");

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Checks = await _diagnostics.RunAsync(cancellationToken);
        CheckedAt = DateTimeOffset.Now.ToString("HH:mm:ss");
    }
}
