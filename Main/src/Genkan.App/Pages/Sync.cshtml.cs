using Genkan.Sync;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Genkan.Pages;

[Authorize]
public class SyncModel : PageModel
{
    private readonly SyncService _sync;

    public SyncModel(SyncService sync) => _sync = sync;

    public string SyncthingUrl { get; private set; } = string.Empty;

    public bool Configured { get; private set; }

    public bool Reachable { get; private set; }

    public SyncthingStatus? Status { get; private set; }

    public IReadOnlyList<SyncthingFolder> Folders { get; private set; } = Array.Empty<SyncthingFolder>();

    public IReadOnlyList<SyncthingDevice> Devices { get; private set; } = Array.Empty<SyncthingDevice>();

    public string IgnoreTemplate { get; } =
        """
        // 玄关：只同步 src（Syncthing 的 .stignore）
        // 放在项目根目录，Syncthing 会忽略其余一切，只同步 src/。
        *
        !src
        !src/**

        // 顺便挡掉常见的构建垃圾
        (?d)bin
        (?d)obj
        (?d)node_modules
        (?d).git
        (?d)target
        (?d)dist
        (?d)build
        (?d).vs
        (?d).idea
        """;

    public async Task OnGetAsync()
    {
        SyncthingUrl = _sync.Url;
        Configured = _sync.Configured;

        if (!Configured)
        {
            return;
        }

        Status = await _sync.GetStatusAsync(HttpContext.RequestAborted);
        Reachable = Status is not null;

        if (Reachable)
        {
            Folders = await _sync.GetFoldersAsync(HttpContext.RequestAborted);
            Devices = await _sync.GetDevicesAsync(HttpContext.RequestAborted);
        }
    }
}
