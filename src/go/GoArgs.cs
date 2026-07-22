namespace Devlooped;

public static class GoArgs
{
    public static readonly string[] ReadyToRunPublishArgs = ["/p:PublishAot=false", "/p:PublishReadyToRun=true"];

    static readonly string[] GoSwitchNames = ["debug", "r2r", "gdbg"];
    static readonly HashSet<string> Subcommands = new(StringComparer.OrdinalIgnoreCase) { "dev", "clean", "check", "skill" };

    static string[]? forwardArgs;

    /// <summary>Arguments stripped before CAF parsing; forwarded to the target app.</summary>
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
                // Attached value forms: --go-r2r:true / --go-r2r=true
                var sep = name.IndexOfAny([':', '=']);
                if (sep >= 0)
                {
                    var baseName = name[..sep];
                    if (GoSwitchNames.Any(s => s.Equals(baseName, StringComparison.OrdinalIgnoreCase)))
                    {
                        result.Add("--" + baseName.ToLowerInvariant() + name[sep..]);
                        continue;
                    }
                }
                else if (GoSwitchNames.Any(s => s.Equals(name, StringComparison.OrdinalIgnoreCase)))
                {
                    result.Add("--" + name.ToLowerInvariant());
                    continue;
                }
            }
            else if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                var name = arg[2..];
                var sep = name.IndexOfAny([':', '=']);
                if (sep >= 0)
                {
                    var baseName = name[..sep];
                    if (GoSwitchNames.Any(s => s.Equals(baseName, StringComparison.OrdinalIgnoreCase)))
                    {
                        result.Add("--" + baseName.ToLowerInvariant() + name[sep..]);
                        continue;
                    }
                }
                else if (GoSwitchNames.Any(s => s.Equals(name, StringComparison.OrdinalIgnoreCase)))
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
    /// and pass-through app arguments. Go options such as <c>--r2r</c>
    /// stay in the CAF slice; every other trailing token becomes an app argument.
    /// </summary>
    internal static string[] PrepareArgs(string[] args)
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
            subcommand = args[0].ToLowerInvariant();
            index = 1;
        }

        // clean / check / skill (and nested skill remove) own their args; do not split for app forwarding.
        if (subcommand is "clean" or "check" or "skill")
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

    public static string[] ApplyPublishMode(string[] dotnetArgs, bool readyToRun)
        => readyToRun ? [.. ReadyToRunPublishArgs, .. dotnetArgs] : dotnetArgs;

    /// <summary>
    /// Appends quiet MSBuild verbosity (<c>-v:quiet</c>) for the underlying <c>dotnet publish</c>/<c>dotnet run</c>.
    /// </summary>
    public static string[] ApplyVerbosity(string[] dotnetArgs)
        => [.. dotnetArgs, "-v:quiet"];
}
