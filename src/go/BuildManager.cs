using System.Diagnostics.CodeAnalysis;

namespace Devlooped;

public record BuildState(string? App, string? Bin, IReadOnlyList<string> Inputs)
{
    public static bool TryRead(string path, [NotNullWhen(true)] out BuildState? state)
    {
        state = null;
        if (!File.Exists(path))
            return false;

        string? app = null;
        string? bin = null;
        var inputs = new List<string>();

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
        }

        if (inputs.Count == 0)
            return false;

        state = new BuildState(app, bin, inputs);
        return true;
    }
}

public static class BuildManager
{
    public static bool IsUpToDate(BuildState state, string artifact)
    {
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