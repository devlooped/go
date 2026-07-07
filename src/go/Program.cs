using System.Text;
using ConsoleAppFramework;
using Devlooped;

var app = ConsoleApp.Create();
app.Add("", RunAsync);
app.Add("dev", DevAsync);
app.Add("clean", CleanAsync);
app.Add<CleanupCommands>();
await app.RunAsync(args);

/// <summary>
/// Runs a file-based .NET app from a .cs entrypoint.
/// </summary>
/// <param name="input">Path to an existing .cs file.</param>
/// <param name="r2r">Publish with ReadyToRun instead of native AOT; supports more dynamic .NET features while keeping most publish optimizations.</param>
/// <param name="extraArgs">Arguments before '--' are passed to 'dotnet publish'; arguments after '--' are forwarded to the published app. Without '--', all extra arguments are forwarded to the published app.
/// </param>
static async Task<int> RunAsync([Argument] string input, bool r2r = false, [Argument] params string[] extraArgs)
{
    var context = Prepare(input, extraArgs, r2r);
    if (context is null)
        return 1;

    var (dotnet, cs, publishDir, stamp, targets, mode, dotnetArgs, appArgs) = context.Value;

    if (BuildState.TryRead(stamp, out var state) &&
        state.App is not null &&
        BuildManager.IsUpToDate(state, state.App, mode))
        return await ExecuteAppAsync(publishDir, () => ProcessRunner.RunAsync(state.App, appArgs));

    File.WriteAllText(stamp, BuildState.InitialContent(mode), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

    var exit = await ProcessRunner.PublishAsync(dotnet, cs, stamp, targets, publishDir, dotnetArgs);
    if (exit != 0)
        return exit;

    if (!BuildState.TryRead(stamp, out state) || state.App is null)
    {
        ConsoleApp.LogError("Published app not found.");
        return 1;
    }

    return await ExecuteAppAsync(publishDir, () => ProcessRunner.RunAsync(state.App, appArgs));
}

/// <summary>
/// Deletes cached publish artifacts for a file-based .NET app.
/// </summary>
/// <param name="input">Path to an existing .cs file.</param>
/// <param name="all">Delete cached artifacts for all apps instead.</param>
static int CleanAsync([Argument] string? input = null, bool all = false)
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
        ConsoleApp.LogError("Specify a .cs file to clean, or --all to clean all cached apps.");
        return 1;
    }

    if (!TryResolveEntryPoint(input, out var publishDir, out var stamp))
        return 1;

    return AppCleaner.Clean(publishDir, stamp);
}

/// <summary>
/// Runs a file-based .NET app from a .cs entrypoint using dotnet run for fast iteration.
/// </summary>
/// <param name="input">Path to an existing .cs file.</param>
/// <param name="extraArgs">Arguments before '--' are passed to 'dotnet run'; arguments after '--' are forwarded to the app. Without '--', all extra arguments are forwarded to the app.
/// </param>
static async Task<int> DevAsync([Argument] string input, [Argument] params string[] extraArgs)
{
    var context = Prepare(input, extraArgs);
    if (context is null)
        return 1;

    var (dotnet, cs, publishDir, stamp, targets, _, dotnetArgs, appArgs) = context.Value;

    if (BuildState.TryRead(stamp, out var state) &&
        state.Bin is not null &&
        BuildManager.IsUpToDate(state, state.Bin))
        return await ExecuteAppAsync(publishDir, () => ProcessRunner.DotnetExecAsync(dotnet, state.Bin, appArgs));

    File.WriteAllText(stamp, string.Empty, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

    return await ExecuteAppAsync(publishDir, () => ProcessRunner.DotnetRunAsync(dotnet, cs, stamp, targets, dotnetArgs, appArgs));
}

static async Task<int> ExecuteAppAsync(string publishDir, Func<Task<int>> execute)
{
    Directory.Touch(publishDir);
    var cleanup = CleanupScheduler.TryScheduleAsync();
    var exit = await execute();
    await cleanup;
    return exit;
}

static bool TryResolveEntryPoint(string input, out string publishDir, out string stamp)
{
    var cs = Path.GetFullPath(input);
    if (!File.Exists(cs))
    {
        ConsoleApp.LogError($"File not found: {cs}");
        publishDir = string.Empty;
        stamp = string.Empty;
        return false;
    }

    publishDir = Directory.GetPublishDir(cs);
    stamp = Path.Combine(publishDir, "go.stamp");
    return true;
}

static (string Dotnet, string Cs, string PublishDir, string Stamp, string Targets, PublishMode Mode, string[] DotnetArgs, string[] AppArgs)? Prepare(string input, string[] extraArgs, bool readyToRun = false)
{
    var dotnet = DotnetMuxer.Path?.FullName;
    if (dotnet is null)
    {
        ConsoleApp.LogError("dotnet executable not found.");
        return null;
    }

    if (!TryResolveEntryPoint(input, out var publishDir, out var stamp))
        return null;

    var cs = Path.GetFullPath(input);

    var (dotnetArgs, appArgs) = GoArgs.Split(extraArgs);
    var mode = readyToRun ? PublishMode.R2r : PublishMode.Aot;
    dotnetArgs = GoArgs.ApplyPublishMode(dotnetArgs, readyToRun);
    var targets = Path.Combine(AppContext.BaseDirectory, "go.targets");

    return (dotnet, cs, publishDir, stamp, targets, mode, dotnetArgs, appArgs);
}