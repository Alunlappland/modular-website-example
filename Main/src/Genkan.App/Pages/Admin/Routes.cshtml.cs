using Genkan;
using Genkan.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Genkan.Pages.Admin;

[Authorize(Roles = "admin")]
public class RoutesModel : PageModel
{
    private readonly ExtendRegistry _registry;
    private readonly ExtendStore _store;
    private readonly GenkanPaths _paths;
    private readonly AuditLog _audit;
    private readonly ILogger<RoutesModel> _logger;

    public RoutesModel(
        ExtendRegistry registry,
        ExtendStore store,
        GenkanPaths paths,
        AuditLog audit,
        ILogger<RoutesModel> logger)
    {
        _registry = registry;
        _store = store;
        _paths = paths;
        _audit = audit;
        _logger = logger;
    }

    public IReadOnlyList<ExtendEntry> Mounts { get; private set; } = Array.Empty<ExtendEntry>();

    [BindProperty]
    public string Id { get; set; } = string.Empty;

    [BindProperty]
    public string Title { get; set; } = string.Empty;

    [BindProperty]
    public string Route { get; set; } = string.Empty;

    [BindProperty]
    public string Kind { get; set; } = "static";

    [BindProperty]
    public string? TargetUrl { get; set; }

    [BindProperty]
    public string? Permission { get; set; }

    [BindProperty]
    public int Order { get; set; } = 100;

    [BindProperty]
    public bool Enabled { get; set; } = true;

    public string? Error { get; private set; }

    public string? Info { get; private set; }

    public void OnGet(string? edit = null)
    {
        Load();

        if (string.IsNullOrWhiteSpace(edit))
        {
            return;
        }

        var entry = _registry.FindById(edit);
        if (entry is null)
        {
            return;
        }

        Id = entry.Id;
        Title = entry.Title;
        Route = entry.Route;
        Kind = entry.Kind;
        TargetUrl = entry.Url;
        Permission = entry.Permission;
        Order = entry.Order;
        Enabled = entry.Enabled;
    }

    public IActionResult OnPostSave()
    {
        var input = new MountInput(
            (Id ?? string.Empty).Trim().ToLowerInvariant(),
            (Title ?? string.Empty).Trim(),
            (Route ?? string.Empty).Trim(),
            Kind,
            TargetUrl,
            Permission,
            Order,
            Enabled);

        var errors = _store.Save(input);
        if (errors.Count > 0)
        {
            Error = string.Join(" ", errors);
            Load();
            return Page();
        }

        _registry.Reload(_paths);
        _audit.Write(HttpContext, "routes.save", input.Id, "ok");
        Info = $"已保存「{input.Title}」，路由 {input.Route} 立即生效。";
        Load();
        return Page();
    }

    public IActionResult OnPostDelete(string id)
    {
        if (_store.Delete(id))
        {
            _registry.Reload(_paths);
            _audit.Write(HttpContext, "routes.delete", id, "ok");
            Info = $"已删除 {id}。";
        }
        else
        {
            Error = "删除失败：ID 不存在或路径非法。";
        }

        Load();
        return Page();
    }

    public IActionResult OnPostToggle(string id)
    {
        var entry = _registry.FindById(id);
        if (entry is null)
        {
            Error = "找不到这个挂载。";
            Load();
            return Page();
        }

        if (_store.SetEnabled(id, !entry.Enabled))
        {
            _registry.Reload(_paths);
            _audit.Write(HttpContext, "routes.toggle", id, "ok", detail: entry.Enabled ? "disable" : "enable");
            Info = $"已{(entry.Enabled ? "停用" : "启用")} {id}。";
        }

        Load();
        return Page();
    }

    private void Load() => Mounts = _registry.Entries;
}
