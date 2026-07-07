namespace Devlooped;

public static class GoArgs
{
    public static readonly string[] ReadyToRunPublishArgs = ["/p:PublishAot=false", "/p:PublishReadyToRun=true"];

    static readonly string[] GoSwitchNames = ["force", "debug", "r2r"];

    /// <summary>
    /// Normalizes go-specific switches so both prefix-less (--force) and --go- forms (--go-force)
    /// are accepted; maps --go-* aliases to the bare form the command methods declare.
    /// </summary>
    public static string[] Normalize(string[] args)
    {
        if (args is null or { Length: 0 })
            return args ?? [];

        var result = new List<string>(args.Length);
        foreach (var arg in args)
        {
            if (arg.StartsWith("--go-", StringComparison.OrdinalIgnoreCase))
            {
                var name = arg[5..];
                if (GoSwitchNames.Any(s => s.Equals(name, StringComparison.OrdinalIgnoreCase)))
                {
                    result.Add("--" + name.ToLowerInvariant());
                    continue;
                }
            }
            else if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                var name = arg[2..];
                if (GoSwitchNames.Any(s => s.Equals(name, StringComparison.OrdinalIgnoreCase)))
                {
                    result.Add("--" + name.ToLowerInvariant());
                    continue;
                }
            }
            result.Add(arg);
        }
        return [.. result];
    }

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

    public static string[] ApplyPublishMode(string[] dotnetArgs, bool readyToRun)
        => readyToRun ? [.. ReadyToRunPublishArgs, .. dotnetArgs] : dotnetArgs;
}