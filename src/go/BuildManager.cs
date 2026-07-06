using System.Diagnostics.CodeAnalysis;

namespace Devlooped;

public record BuildState(string App, IReadOnlyList<string> Inputs)
{
    public static bool TryRead(string path, [NotNullWhen(true)] out BuildState? state)
    {
        state = null;
        if (!File.Exists(path))
            return false;

        string? app = null;
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
            else if (key == "input")
                inputs.Add(value);
        }

        if (app is null || !File.Exists(app))
            return false;

        state = new BuildState(app, inputs);
        return true;
    }
}

public static class BuildManager
{
    public static bool IsUpToDate(BuildState state)
    {
        if (string.IsNullOrWhiteSpace(state.App) || !File.Exists(state.App))
            return false;

        if (state.Inputs.Count == 0)
            return false;

        var appTime = File.GetLastWriteTimeUtc(state.App);
        foreach (var input in state.Inputs)
        {
            if (!File.Exists(input))
                return false;

            if (File.GetLastWriteTimeUtc(input) > appTime)
                return false;
        }

        return true;
    }
}
