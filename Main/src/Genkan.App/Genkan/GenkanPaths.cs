namespace Genkan;

/// <summary>
/// 站点的物理路径约定。所有目录都从站点根目录推导，方便整体搬迁与备份。
/// </summary>
public sealed class GenkanPaths
{
    public string Root { get; }
    public string Main { get; }
    public string Src { get; }
    public string Bin { get; }
    public string Conf { get; }
    public string Data { get; }
    public string Database { get; }
    public string Document { get; }
    public string Depend { get; }
    public string Logs { get; }
    public string Website { get; }
    public string Pages { get; }
    public string Assets { get; }
    public string Themes { get; }
    public string Extend { get; }

    /// <summary>分块上传的临时拼装目录。</summary>
    public string Uploads { get; }

    public GenkanPaths(string root)
    {
        Root = Path.GetFullPath(root).TrimEnd('\\', '/');
        Main = Path.Combine(Root, "Main");
        Src = Path.Combine(Main, "src");
        Bin = Path.Combine(Main, "bin");
        Conf = Path.Combine(Main, "conf");
        Data = Path.Combine(Main, "data");
        Database = Path.Combine(Main, "database");
        Document = Path.Combine(Main, "document");
        Depend = Path.Combine(Main, "depend");
        Logs = Path.Combine(Main, "logs");
        Website = Path.Combine(Main, "website");
        Pages = Path.Combine(Website, "pages");
        Assets = Path.Combine(Website, "assets");
        Themes = Path.Combine(Website, "themes");
        Extend = Path.Combine(Root, "Extend");
        Uploads = Path.Combine(Depend, "tmp", "uploads");
    }

    public void EnsureCreated()
    {
        foreach (var dir in new[]
        {
            Main, Src, Bin, Conf, Data, Database, Document, Depend, Logs,
            Website, Pages, Assets, Themes, Extend, Uploads,
        })
        {
            Directory.CreateDirectory(dir);
        }
    }
}
