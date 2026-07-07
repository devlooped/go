using System.Net.Http;
using System.Text;
using ConsoleAppFramework;
using Devlooped;

var app = ConsoleApp.Create();
app.Add("", RunAsync);
app.Add("dev", DevAsync);
app.Add("clean", CleanAsync);
app.Add<CleanupCommands>();
await app.RunAsync(GoArgs.Normalize(args));

/// <summary>
/// Runs a file-based .NET app from a .cs entrypoint.
/// </summary>
/// <param name="input">Path to an existing .cs file or remote ref (owner/repo[@ref][:path]).</param>
/// <param name="r2r">Publish with ReadyToRun instead of native AOT; supports more dynamic .NET features while keeping most publish optimizations. Alias: --go-r2r.</param>
/// <param name="debug">Launch debugger before executing. Alias: --go-debug.</param>
/// <param name="extraArgs">Arguments before '--' are passed to 'dotnet publish'; arguments after '--' are forwarded to the published app. Without '--', all extra arguments are forwarded to the published app.
/// </param>
static async Task<int> RunAsync([Argument] string input, bool r2r = false, bool debug = false, [Argument] params string[] extraArgs)
{
    if (debug)
        System.Diagnostics.Debugger.Launch();

    var source = await GetEffectiveSourceAsync(input);
    if (source is null)
        return 1;

    var context = Prepare(source, extraArgs, r2r);
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
/// Deletes cached publish artifacts for a file-based .NET app, or for a remote ref: deletes the bundle and also its associated publish artifacts for every previously-used path recorded in the bundle's ETags/Entry (:path on the ref is ignored).
/// </summary>
/// <param name="input">Path to an existing .cs file or remote ref (owner/repo[@ref][:path]).</param>
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
        ConsoleApp.LogError("Specify a .cs file or remote ref to clean, or --all to clean all cached apps.");
        return 1;
    }

    var full = Path.GetFullPath(input);
    if (File.Exists(full))
    {
        if (!TryResolveEntryPoint(input, out _, out var publishDir, out var stamp))
            return 1;

        return AppCleaner.Clean(publishDir, stamp);
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
                        if (TryResolveEntryPoint(filePath, out _, out var publishDir, out var stamp))
                            AppCleaner.Clean(publishDir, stamp);
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

    if (!TryResolveEntryPoint(input, out _, out var pdir, out var stmp))
        return 1;

    return AppCleaner.Clean(pdir, stmp);
}

/// <summary>
/// Runs a file-based .NET app from a .cs entrypoint using dotnet run for fast iteration.
/// </summary>
/// <param name="input">Path to an existing .cs file or remote ref (owner/repo[@ref][:path]).</param>
/// <param name="r2r">Accepted for consistency (ignored for dev which uses dotnet run). Alias: --go-r2r.</param>
/// <param name="debug">Launch debugger before executing. Alias: --go-debug.</param>
/// <param name="extraArgs">Arguments before '--' are passed to 'dotnet run'; arguments after '--' are forwarded to the app. Without '--', all extra arguments are forwarded to the app.
/// </param>
static async Task<int> DevAsync([Argument] string input, bool r2r = false, bool debug = false, [Argument] params string[] extraArgs)
{
    if (debug)
        System.Diagnostics.Debugger.Launch();

    var source = await GetEffectiveSourceAsync(input);
    if (source is null)
        return 1;

    var context = Prepare(source, extraArgs);
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

static bool TryResolveEntryPoint(string input, out string effectiveCs, out string publishDir, out string stamp)
    => RemoteSourceResolver.TryResolveEntryPoint(input, out effectiveCs, out publishDir, out stamp);

static (string Dotnet, string Cs, string PublishDir, string Stamp, string Targets, PublishMode Mode, string[] DotnetArgs, string[] AppArgs)? Prepare(string input, string[] extraArgs, bool readyToRun = false)
{
    var dotnet = DotnetMuxer.Path?.FullName;
    if (dotnet is null)
    {
        ConsoleApp.LogError("dotnet executable not found.");
        return null;
    }

    if (!TryResolveEntryPoint(input, out var cs, out var publishDir, out var stamp))
        return null;

    var (dotnetArgs, appArgs) = GoArgs.Split(extraArgs);
    var mode = readyToRun ? PublishMode.R2r : PublishMode.Aot;
    dotnetArgs = GoArgs.ApplyPublishMode(dotnetArgs, readyToRun);
    var targets = Path.Combine(AppContext.BaseDirectory, "go.targets");

    return (dotnet, cs, publishDir, stamp, targets, mode, dotnetArgs, appArgs);
}

static async Task<string?> GetEffectiveSourceAsync(string input)
    => await RemoteSourceResolver.GetEffectiveSourceAsync(input);