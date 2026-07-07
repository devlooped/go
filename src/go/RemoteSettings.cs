using System.Text;
using System.Text.Json.Serialization;
using Tomlyn;
using Tomlyn.Serialization;

namespace Devlooped;

public class RemoteSettings
{
    public string? Entry { get; set; }

    /// <summary>
    /// ETags keyed by the relative file path within the bundle (e.g. "program.cs" or "src/hello.cs")
    /// or equivalently the path portion from a remote ref (owner/repo:path).
    /// The go.toml at the bundle root holds ETags for all such refs/entries within the same bundle.
    /// </summary>
    public Dictionary<string, string>? ETags { get; set; }
}

public static class RemoteSettingsStore
{
    public static string GetFilePath(string directory)
        => Path.Combine(directory, "go.toml");

    public static RemoteSettings Load(string? path = null)
    {
        if (string.IsNullOrEmpty(path))
            return new RemoteSettings();

        path = ResolveTomlPath(path);

        if (!File.Exists(path))
            return new RemoteSettings();

        var text = File.ReadAllText(path);
        return TomlSerializer.Deserialize(text, TomlContext.Default.RemoteSettings) ?? new RemoteSettings();
    }

    public static void Save(RemoteSettings settings, string? path = null)
    {
        if (string.IsNullOrEmpty(path))
            path = "go.toml";

        path = ResolveTomlPath(path);

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateUserDirectory(dir);

        var text = TomlSerializer.Serialize(settings, TomlContext.Default.RemoteSettings);
        File.WriteAllText(path, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    public static string? GetETag(RemoteSettings settings, string key)
    {
        if (settings.ETags == null) return null;
        if (settings.ETags.TryGetValue(key, out var value)) return value;
        // Case-insensitive fallback for robustness across platforms/casing in refs
        foreach (var kv in settings.ETags)
            if (string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase))
                return kv.Value;
        return null;
    }

    public static void SetETag(RemoteSettings settings, string key, string etag)
    {
        settings.ETags ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        settings.ETags[key] = etag;
    }

    static string ResolveTomlPath(string path)
    {
        // Treat as directory if it exists as a directory or doesn't look like a .toml file path
        if (Directory.Exists(path) || !path.EndsWith(".toml", StringComparison.OrdinalIgnoreCase))
            return Path.Combine(path, "go.toml");
        return path;
    }
}

[TomlSerializable(typeof(RemoteSettings))]
partial class TomlContext : TomlSerializerContext { }
