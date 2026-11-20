namespace Genkan;

/// <summary>站点是否已完成初始化（有管理员账号）。启动时算一次，setup 成功后翻转。</summary>
public sealed class SetupState
{
    public bool Initialized { get; set; }
}
