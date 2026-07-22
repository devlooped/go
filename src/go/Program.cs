using System.Text;
using ConsoleAppFramework;
using Devlooped;
using Spectre.Console;

if (Environment.GetEnvironmentVariable("GO_DEBUG") == "1")
    System.Diagnostics.Debugger.Launch();

var app = ConsoleApp.Create();
app.Add("", RunAsync);
app.Add("dev", DevAsync);
app.Add("clean", CleanAsync);
app.Add("remove", RemoveAsync);
app.Add<CheckCommands>();
app.Add<CleanupCommands>();
app.Add<SkillCommands>();

var prepared = GoArgs.PrepareArgs(args);
// Zero-arg with no prior runs: show help (same as -h/--help/-?). With history, default command opens the MRU picker.
if (prepared.Length == 0 && RunHistory.List().Count == 0)
    prepared = ["--help"];

await app.RunAsync(prepared);

/// <summary>Runs a file-based .NET app from a .cs entrypoint.</summary>
/// <param name="input">Path to an existing .cs file or remote ref (owner/repo[@ref][:path]). When omitted, selects from previous runs (MRU) then prompts for optional app args.</param>
/// <param name="r2r">Publish with ReadyToRun instead of native AOT; supports more dynamic .NET features while keeping most publish optimizations. </param>
/// <param name="gdbg">Launch debugger before executing.</param>
/// <param name="args">Arguments to pass to the app.</param>
static async Task<int> RunAsync([Argument] string? input = null, bool r2r = false, [Hidden] bool gdbg = false, [Argument] params string[] args)
{
    if (gdbg)
        System.Diagnostics.Debugger.Launch();

    // App args come from PrepareArgs stripping (ForwardArgs). Interactive zero-arg flow replaces both input and args.
    var appArgs = GoArgs.ForwardArgs;
    if (string.IsNullOrWhiteSpace(input))
    {
        var picked = TryPickFromHistory(out appArgs);
        if (picked is null)
            return 1;
        input = picked;
    }

    var historyInput = input;
    var source = await GetEffectiveSourceAsync(input);
    if (source is null)
        return 1;

    var context = Prepare(source, appArgs, r2r);
    if (context is null)
        return 1;

    var (dotnet, cs, publishDir, stamp, targets, mode, dotnetArgs, runAppArgs) = context.Value;

    if (BuildState.TryRead(stamp, out var state) &&
        state.App is not null &&
        BuildManager.IsUpToDate(state, state.App, mode))
        return await ExecuteAppAsync(publishDir, historyInput, () => ProcessRunner.RunAsync(state.App, runAppArgs));

    File.WriteAllText(stamp, BuildState.InitialContent(mode), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

    var exit = await ProcessRunner.PublishAsync(dotnet, cs, stamp, targets, publishDir, dotnetArgs);
    if (exit != 0)
        return exit;

    if (!BuildState.TryRead(stamp, out state) || state.App is null)
    {
        ConsoleApp.LogError("Published app not found.");
        return 1;
    }

    return await ExecuteAppAsync(publishDir, historyInput, () => ProcessRunner.RunAsync(state.App, runAppArgs));
}

/// <summary>Runs a file-based .NET app from a .cs entrypoint using dotnet run for fast iteration.</summary>
/// <param name="input">Path to an existing .cs file or remote ref (owner/repo[@ref][:path]).</param>
/// <param name="r2r">Accepted for consistency (ignored for dev which uses dotnet run).</param>
/// <param name="gdbg">Launch debugger before executing.</param>
/// <param name="args">Arguments to pass to the app.</param>
static async Task<int> DevAsync([Argument] string input, [Hidden] bool r2r = false, [Hidden] bool gdbg = false, [Argument] params string[] args)
{
    if (gdbg)
        System.Diagnostics.Debugger.Launch();

    var historyInput = input;
    var source = await GetEffectiveSourceAsync(input);
    if (source is null)
        return 1;

    var context = Prepare(source, GoArgs.ForwardArgs, readyToRun: false);
    if (context is null)
        return 1;

    var (dotnet, cs, publishDir, stamp, targets, _, dotnetArgs, runAppArgs) = context.Value;

    if (BuildState.TryRead(stamp, out var state) &&
        state.Bin is not null &&
        BuildManager.IsUpToDate(state, state.Bin))
        return await ExecuteAppAsync(publishDir, historyInput, () => ProcessRunner.DotnetExecAsync(dotnet, state.Bin, runAppArgs));

    File.WriteAllText(stamp, string.Empty, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

    return await ExecuteAppAsync(publishDir, historyInput, () => ProcessRunner.DotnetRunAsync(dotnet, cs, stamp, targets, dotnetArgs, runAppArgs));
}

/// <summary>Deletes cached publish artifacts for a file-based .NET app, or for a remote ref.</summary>
/// <param name="input">Path to an existing .cs file or remote ref (owner/repo[@ref][:path]).</param>
/// <param name="all">Delete cached artifacts for all apps instead.</param>
/// <param name="gdbg">Launch debugger before executing.</param>
static int CleanAsync([Argument] string? input = null, bool all = false, [Hidden] bool gdbg = false)
{
    if (gdbg)
        System.Diagnostics.Debugger.Launch();

    return CleanArtifacts(input, all, missingInputMessage: "Specify a .cs file or remote ref to clean, or --all to clean all cached apps.");
}

/// <summary>Cleans cached artifacts (same as clean) and removes the entry from MRU history.</summary>
/// <param name="input">Path to an existing .cs file or remote ref (owner/repo[@ref][:path]).</param>
/// <param name="all">Clean all cached apps and clear the entire MRU history.</param>
/// <param name="gdbg">Launch debugger before executing.</param>
static int RemoveAsync([Argument] string? input = null, bool all = false, [Hidden] bool gdbg = false)
{
    if (gdbg)
        System.Diagnostics.Debugger.Launch();

    if (all)
    {
        if (input is not null)
        {
            ConsoleApp.LogError("Specify either a .cs file or --all, not both.");
            return 1;
        }

        var exit = CleanupManager.Cleanup(days: 0);
        RunHistory.Clear();
        return exit;
    }

    if (input is null)
    {
        ConsoleApp.LogError("Specify a .cs file or remote ref to remove, or --all to remove all cached apps and history.");
        return 1;
    }

    // Artifact clean first (same as clean); always drop history identity so MRU/picker stays in sync.
    var cleanExit = CleanArtifacts(input, all: false, missingInputMessage: null);
    RunHistory.Remove(input);
    return cleanExit;
}

/// <summary>Shared artifact cleanup for <c>clean</c> and <c>remove</c>.</summary>
static int CleanArtifacts(string? input, bool all, string? missingInputMessage)
{
    if (all)
    {
        if (input is not null)
        {
            ConsoleApp.LogError("Specify either a .cs file or --all, not both.");
            return 1;
        }

        return CleanupManager.Cleanup(days: 0);
    }

    if (input is null)
    {
        if (missingInputMessage is not null)
            ConsoleApp.LogError(missingInputMessage);
        return 1;
    }

    var full = Path.GetFullPath(input);
    if (File.Exists(full))
    {
        if (!TryResolveEntryPoint(input, out var effectiveCs, out var publishDir, out var stamp))
            return 1;

        return AppCleaner.Clean(publishDir, stamp, effectiveCs);
    }

    if (RemoteRef.TryParse(input, out var remote))
    {
        var bundleDir = remote.TempPath;
        if (Directory.Exists(bundleDir))
        {
            try
            {
                // A bundle can contain multiple ref paths in use (e.g. different :path or discovered entries).
                // ETags keys (and the persisted Entry) record the relative file paths previously used to run.
                // Clean the published artifacts for each by treating them as local file paths (before we nuke the bundle).
                var settings = RemoteSettingsStore.Load(bundleDir);
                var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (!string.IsNullOrEmpty(settings.Entry))
                    keys.Add(settings.Entry.Replace('\\', '/'));
                if (settings.ETags != null)
                {
                    foreach (var k in settings.ETags.Keys)
                        keys.Add(k.Replace('\\', '/'));
                }

                foreach (var key in keys)
                {
                    var filePath = Path.Combine(bundleDir, key.Replace('/', Path.DirectorySeparatorChar));
                    if (File.Exists(filePath))
                    {
                        if (TryResolveEntryPoint(filePath, out var effectiveCs, out var publishDir, out var stamp))
                            AppCleaner.Clean(publishDir, stamp, effectiveCs);
                    }
                }
            }
            catch
            {
                // Best-effort: continue to delete the bundle even if some publish cleans failed.
            }

            try
            {
                Directory.Delete(bundleDir, recursive: true);
                return 0;
            }
            catch
            {
                return 1;
            }
        }

        return 0;
    }

    if (!TryResolveEntryPoint(input, out var cs, out var pdir, out var stmp))
        return 1;

    return AppCleaner.Clean(pdir, stmp, cs);
}

/// <summary>
/// Interactive MRU picker over go.toml history, then optional app-args prompt.
/// Returns null on empty history, cancel, or non-interactive console.
/// </summary>
static string? TryPickFromHistory(out string[] appArgs)
{
    appArgs = [];
    var history = RunHistory.List();
    if (history.Count == 0)
        return null;

    try
    {
        var selected = AnsiConsole.Prompt(
            new SelectionPrompt<RunHistoryEntry>()
                .Title("Select a previous run:")
                .PageSize(15)
                .EnableSearch()
                .MoreChoicesText("[grey](Move up/down to reveal more; type to search)[/]")
                .UseConverter(static e => e.Display)
                .AddChoices(history));

        var argsLine = AnsiConsole.Prompt(
            new TextPrompt<string>("Arguments (optional):")
                .AllowEmpty());

        var run = RunHistory.ToRunInputs(selected.Input, argsLine);
        appArgs = run.AppArgs;
        return run.Input;
    }
    catch (Exception)
    {
        // Spectre throws when stdin is redirected / no interactive terminal.
        ConsoleApp.LogError("Interactive selection requires a terminal.");
        return null;
    }
}

static async Task<int> ExecuteAppAsync(string publishDir, string historyInput, Func<Task<int>> execute)
{
    Directory.Touch(publishDir);
    RunHistory.Record(historyInput);
    var cleanup = CleanupScheduler.TryScheduleAsync();
    var exit = await execute();
    await cleanup;
    return exit;
}

static bool TryResolveEntryPoint(string input, out string effectiveCs, out string publishDir, out string stamp)
    => RemoteSourceResolver.TryResolveEntryPoint(input, out effectiveCs, out publishDir, out stamp);

static (string Dotnet, string Cs, string PublishDir, string Stamp, string Targets, PublishMode Mode, string[] DotnetArgs, string[] AppArgs)? Prepare(string input, string[] appArgs, bool readyToRun = false)
{
    var dotnet = DotnetMuxer.Path?.FullName;
    if (dotnet is null)
    {
        ConsoleApp.LogError("dotnet executable not found.");
        return null;
    }

    if (!TryResolveEntryPoint(input, out var cs, out var publishDir, out var stamp))
        return null;

    var mode = readyToRun ? PublishMode.R2r : PublishMode.Aot;
    var dotnetArgs = GoArgs.ApplyVerbosity(GoArgs.ApplyPublishMode([], readyToRun));
    var targets = Path.Combine(AppContext.BaseDirectory, "go.targets");

    return (dotnet, cs, publishDir, stamp, targets, mode, dotnetArgs, appArgs);
}

static async Task<string?> GetEffectiveSourceAsync(string input)
    => await RemoteSourceResolver.GetEffectiveSourceAsync(input);
