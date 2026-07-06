namespace Devlooped;

public static class GoArgs
{
    public static (string[] Dotnet, string[] App) Split(string[] extraArgs)
    {
        var separator = Array.IndexOf(extraArgs, "--");
        if (separator < 0)
            return ([], extraArgs);

        return (
            [.. extraArgs.AsSpan(0, separator)],
            [.. extraArgs.AsSpan(separator + 1)]
        );
    }
}