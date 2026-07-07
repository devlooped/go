using System.Diagnostics.CodeAnalysis;

namespace Devlooped;

public enum PublishMode
{
    Aot,
    R2r,
}

public record BuildState(string? App, string? Bin, IReadOnlyList<string> Inputs, PublishMode Mode = PublishMode.Aot)
{
    public static string InitialContent(PublishMode mode)
        => $"mode={mode.ToString().ToLowerInvariant()}{Environment.NewLine}";

    public static bool TryRead(string path, [NotNullWhen(true)] out BuildState? state)
    {
        state = null;
        if (!File.Exists(path))
            return false;

        string? app = null;
        string? bin = null;
        var inputs = new List<string>();
        var mode = PublishMode.Aot;

        foreach (var line in File.ReadLines(path))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed[0] is '#' or ';' or '[')
                continue;

            var eq = trimmed.IndexOf('=');
            if (eq <= 0)
                continue;

            var key = trimmed[..eq].Trim();
            var value = trimmed[(eq + 1)..].Trim();

            if (key == "app")
                app = value;
            else if (key == "bin")
                bin = value;
            else if (key == "input")
                inputs.Add(value);
            else if (key == "mode" && TryParseMode(value, out var parsedMode))
                mode = parsedMode;
        }

        if (inputs.Count == 0)
            return false;

        state = new BuildState(app, bin, inputs, mode);
        return true;
    }

    static bool TryParseMode(string value, out PublishMode mode)
    {
        if (value.Equals("r2r", StringComparison.OrdinalIgnoreCase))
        {
            mode = PublishMode.R2r;
            return true;
        }

        if (value.Equals("aot", StringComparison.OrdinalIgnoreCase))
        {
            mode = PublishMode.Aot;
            return true;
        }

        mode = default;
        return false;
    }
}

public static class BuildManager
{
    public static bool IsUpToDate(BuildState state, string artifact, PublishMode mode = PublishMode.Aot)
    {
        if (state.Mode != mode)
            return false;

        if (string.IsNullOrWhiteSpace(artifact) || !File.Exists(artifact))
            return false;

        if (state.Inputs.Count == 0)
            return false;

        var artifactTime = File.GetLastWriteTimeUtc(artifact);
        foreach (var input in state.Inputs)
        {
            if (!File.Exists(input))
                return false;

            if (File.GetLastWriteTimeUtc(input) > artifactTime)
                return false;
        }

        return true;
    }
}