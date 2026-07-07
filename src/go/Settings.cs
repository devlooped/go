using System.Text;
using System.Text.Json.Serialization;
using Tomlyn;
using Tomlyn.Serialization;

namespace Devlooped;

public class Settings
{
    public DateTimeOffset? LastCleanupUtc { get; set; }
}

public static class SettingsStore
{
    static string DefaultPath => System.IO.Path.Combine(Directory.GetTempRoot(), "go.toml");

    public static Settings Load(string? path = null)
    {
        path ??= DefaultPath;
        if (!File.Exists(path))
            return new Settings();

        var text = File.ReadAllText(path);
        return TomlSerializer.Deserialize(text, TomlContext.Default.Settings) ?? new Settings();
    }

    public static void Save(Settings settings, string? path = null)
    {
        path ??= DefaultPath;
        var text = TomlSerializer.Serialize(settings, TomlContext.Default.Settings);
        File.WriteAllText(path, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }
}

[TomlSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[TomlSerializable(typeof(Settings))]
partial class TomlContext : TomlSerializerContext { }