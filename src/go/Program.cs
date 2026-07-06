using System.Text;
using ConsoleAppFramework;
using Devlooped;

var app = ConsoleApp.Create();
app.Add("", RunAsync);
app.Add("dev", DevAsync);
await app.RunAsync(args);

/// <summary>
/// Runs a file-based .NET app from a .cs entrypoint.
/// </summary>
/// <param name="input">Path to an existing .cs file.</param>
/// <param name="extraArgs">Arguments before '--' are passed to 'dotnet publish'; arguments after '--' are forwarded to the published app. Without '--', all extra arguments are forwarded to the published app.
/// </param>
static async Task<int> RunAsync([Argument] string input, [Argument] params string[] extraArgs)
{
    var context = Prepare(input, extraArgs);
    if (context is null)
        return 1;

    var (dotnet, cs, stamp, targets, dotnetArgs, appArgs) = context.Value;

    if (BuildState.TryRead(stamp, out var state) &&
        state.App is not null &&
        BuildManager.IsUpToDate(state, state.App))
        return await ProcessRunner.RunAsync(state.App, appArgs);

    File.WriteAllText(stamp, string.Empty, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

    var exit = await ProcessRunner.PublishAsync(dotnet, cs, stamp, targets, dotnetArgs);
    if (exit != 0)
        return exit;

    if (!BuildState.TryRead(stamp, out state) || state.App is null)
    {
        ConsoleApp.LogError("Published app not found.");
        return 1;
    }

    return await ProcessRunner.RunAsync(state.App, appArgs);
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

    var (dotnet, cs, stamp, targets, dotnetArgs, appArgs) = context.Value;

    if (BuildState.TryRead(stamp, out var state) &&
        state.Bin is not null &&
        BuildManager.IsUpToDate(state, state.Bin))
        return await ProcessRunner.DotnetExecAsync(dotnet, state.Bin, appArgs);

    File.WriteAllText(stamp, string.Empty, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

    return await ProcessRunner.DotnetRunAsync(dotnet, cs, stamp, targets, dotnetArgs, appArgs);
}

static (string Dotnet, string Cs, string Stamp, string Targets, string[] DotnetArgs, string[] AppArgs)? Prepare(string input, string[] extraArgs)
{
    var dotnet = DotnetMuxer.Path?.FullName;
    if (dotnet is null)
    {
        ConsoleApp.LogError("dotnet executable not found.");
        return null;
    }

    var cs = Path.GetFullPath(input);
    if (!File.Exists(cs))
    {
        ConsoleApp.LogError($"File not found: {cs}");
        return null;
    }

    var (dotnetArgs, appArgs) = GoArgs.Split(extraArgs);
    var stamp = Path.ChangeExtension(cs, "stamp");
    var targets = Path.Combine(AppContext.BaseDirectory, "go.targets");

    return (dotnet, cs, stamp, targets, dotnetArgs, appArgs);
}