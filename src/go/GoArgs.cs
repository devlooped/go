namespace Devlooped;

public static class GoArgs
{
    public static readonly string[] ReadyToRunPublishArgs = ["/p:PublishAot=false", "/p:PublishReadyToRun=true"];

    static readonly string[] GoSwitchNames = ["debug", "r2r", "gdbg"];
    static readonly HashSet<string> Subcommands = new(StringComparer.OrdinalIgnoreCase) { "dev", "clean" };

    static string[]? forwardArgs;

    /// <summary>Arguments stripped before CAF parsing; forward to dotnet and/or the target app via <see cref="Split"/>.</summary>
    internal static string[] ForwardArgs => forwardArgs ?? [];

    /// <summary>
    /// Normalizes go-specific switches so both prefix-less and --go- forms
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

    /// <summary>
    /// Splits raw invocation args into a CAF-safe slice (subcommand, input, go flags only)
    /// and pass-through args for dotnet publish/run and the target app.
    /// <para>
    /// Under <c>dnx go</c>, the first <c>--</c> is go's dotnet/app separator — not a CAF escape.
    /// Dotnet options like <c>/p:MyProp=true</c> or <c>-v:q</c> must not reach CAF or they are
    /// rejected as unrecognized options.
    /// </para>
    /// </summary>
    internal static string[] PrepareCafArgs(string[] args)
    {
        args = Normalize(args);
        if (args.Length == 0)
        {
            forwardArgs = [];
            return args;
        }

        if (args.Any(static a => a is "-h" or "--help" or "--version"))
        {
            forwardArgs = [];
            return args;
        }

        var index = 0;
        string? subcommand = null;
        if (Subcommands.Contains(args[0]))
        {
            subcommand = args[0].Equals("dev", StringComparison.OrdinalIgnoreCase) ? "dev" : "clean";
            index = 1;
        }

        if (subcommand == "clean")
        {
            forwardArgs = [];
            return args;
        }

        var caf = new List<string>();
        if (subcommand == "dev")
            caf.Add("dev");

        string? input = null;
        var forward = new List<string>();
        var goFlags = new List<string>();

        for (var i = index; i < args.Length; i++)
        {
            var arg = args[i];
            if (TryMatchGoFlag(arg, out var cafFlag))
            {
                goFlags.Add(cafFlag);
                continue;
            }

            if (input is null)
            {
                input = arg;
                continue;
            }

            forward.Add(arg);
        }

        if (input is not null)
            caf.Add(input);

        caf.AddRange(goFlags);

        forwardArgs = forward.Count == 0 ? [] : [.. forward];
        return [.. caf];
    }

    static bool TryMatchGoFlag(string arg, out string cafFlag)
    {
        cafFlag = "";
        if (!arg.StartsWith("--", StringComparison.Ordinal))
            return false;

        var name = arg[2..];
        if (name.Equals("r2r", StringComparison.OrdinalIgnoreCase))
        {
            cafFlag = "--r2r";
            return true;
        }

        if (name is "debug" or "gdbg")
        {
            cafFlag = "--gdbg";
            return true;
        }

        return false;
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

    public static string[] ApplyDefaultVerbosity(string[] dotnetArgs)
        => HasVerbosityArg(dotnetArgs) ? dotnetArgs : [.. dotnetArgs, "-v:q"];

    static bool HasVerbosityArg(IReadOnlyList<string> args)
    {
        for (var i = 0; i < args.Count; i++)
        {
            if (IsVerbosityArg(args[i]))
                return true;
        }

        return false;
    }

    static bool IsVerbosityArg(string arg)
    {
        if (arg.Length < 2)
            return false;

        ReadOnlySpan<char> span = arg;
        if (span[0] == '/')
            span = span[1..];
        else if (span.StartsWith("--", StringComparison.Ordinal))
            span = span[2..];
        else if (span[0] == '-')
            span = span[1..];
        else
            return false;

        if (span.StartsWith("v:", StringComparison.OrdinalIgnoreCase) ||
            span.StartsWith("v=", StringComparison.OrdinalIgnoreCase))
            return true;

        return span.Equals("v", StringComparison.OrdinalIgnoreCase) ||
               span.Equals("verbosity", StringComparison.OrdinalIgnoreCase) ||
               span.StartsWith("verbosity:", StringComparison.OrdinalIgnoreCase) ||
               span.StartsWith("verbosity=", StringComparison.OrdinalIgnoreCase);
    }
}