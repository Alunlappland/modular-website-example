using System.Runtime.InteropServices;

namespace Genkan.Sys;

/// <summary>在服务器上响一声，用于「有人请求授权」的提示。服务运行在 Session 0 时可能听不到，页面里还有浏览器提示音。</summary>
public static class AudioAlert
{
    [DllImport("user32.dll")]
    private static extern bool MessageBeep(uint type);

    public static bool Ping()
    {
        try
        {
            // 0xFFFFFFFF = 标准警告音
            return MessageBeep(0xFFFFFFFF);
        }
        catch (Exception)
        {
            return false;
        }
    }
}
