using System.Text.Json;

namespace Genkan.Theme;

public sealed class ThemeSettings
{
    public bool Effects { get; set; } = true;
    public string Font { get; set; } = "serif";

    /// <summary>背景图左右留白，而不是铺满整屏。</summary>
    public bool InsetBackground { get; set; } = true;
}

/// <summary>「里」主题的外观偏好：特效开关、字体、背景图（背景图只存服务器，不进 git）。</summary>
public sealed class ThemeService
{
    private static readonly string[] AllowedExtensions = { ".jpg", ".jpeg", ".png", ".webp", ".gif" };

    private readonly GenkanPaths _paths;
    private readonly ILogger<ThemeService> _logger;

    public ThemeService(GenkanPaths paths, ILogger<ThemeService> logger)
    {
        _paths = paths;
        _logger = logger;
    }

    private string ConfigPath => Path.Combine(_paths.Conf, "theme.json");

    public string BackgroundPath
    {
        get
        {
            var directory = Path.Combine(_paths.Data, "themes");
            if (!Directory.Exists(directory))
            {
                return string.Empty;
            }

            foreach (var file in Directory.EnumerateFiles(directory, "background.*"))
            {
                return file;
            }

            return string.Empty;
        }
    }

    public bool HasBackground => BackgroundPath.Length > 0;

    public long BackgroundVersion => HasBackground ? File.GetLastWriteTimeUtc(BackgroundPath).Ticks : 0;

    public ThemeSettings Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                return JsonSerializer.Deserialize<ThemeSettings>(File.ReadAllText(ConfigPath)) ?? new ThemeSettings();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "读取主题配置失败。");
        }

        return new ThemeSettings();
    }

    public void Save(ThemeSettings settings)
    {
        Directory.CreateDirectory(_paths.Conf);
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
    }

    public async Task<(bool Ok, string Message)> SaveBackgroundAsync(IFormFile file, CancellationToken cancellationToken)
    {
        if (file.Length == 0)
        {
            return (false, "没有选择文件。");
        }

        if (file.Length > 8L * 1024 * 1024)
        {
            return (false, "背景图最大 8MB。");
        }

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(extension))
        {
            return (false, "只支持 jpg/png/webp/gif。");
        }

        var directory = Path.Combine(_paths.Data, "themes");
        Directory.CreateDirectory(directory);

        RemoveBackground();

        var target = Path.Combine(directory, "background" + extension);
        await using (var stream = File.Create(target))
        {
            await file.CopyToAsync(stream, cancellationToken);
        }

        return (true, "背景图已更新。");
    }

    public void RemoveBackground()
    {
        var directory = Path.Combine(_paths.Data, "themes");
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(directory, "background.*"))
        {
            try { File.Delete(file); } catch { }
        }
    }

    public string ContentType
        => Path.GetExtension(BackgroundPath).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            _ => "image/jpeg",
        };
}
