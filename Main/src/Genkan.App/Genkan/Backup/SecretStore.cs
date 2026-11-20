using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace Genkan.Backup;

/// <summary>敏感配置（kopia 仓库密码、rclone 凭据等）用 DataProtection 加密后存 conf\secrets.json。</summary>
public sealed class SecretStore
{
    private readonly string _path;
    private readonly IDataProtector _protector;
    private readonly object _gate = new();

    public SecretStore(GenkanPaths paths, IDataProtectionProvider provider)
    {
        _path = Path.Combine(paths.Conf, "secrets.json");
        _protector = provider.CreateProtector("genkan.secrets.v1");
    }

    public bool Has(string name)
    {
        lock (_gate)
        {
            return Load().ContainsKey(name);
        }
    }

    public string? Get(string name)
    {
        lock (_gate)
        {
            return Load().TryGetValue(name, out var value) ? Unprotect(value) : null;
        }
    }

    public void Set(string name, string value)
    {
        lock (_gate)
        {
            var map = Load();
            map[name] = _protector.Protect(value);
            Save(map);
        }
    }

    public void Remove(string name)
    {
        lock (_gate)
        {
            var map = Load();
            if (map.Remove(name))
            {
                Save(map);
            }
        }
    }

    private Dictionary<string, string> Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            var json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                   ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private void Save(Dictionary<string, string> map)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(map, new JsonSerializerOptions { WriteIndented = true }));
    }

    private string Unprotect(string value)
    {
        try
        {
            return _protector.Unprotect(value);
        }
        catch
        {
            return string.Empty;
        }
    }
}
