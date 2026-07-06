using System.Text;
using ConsoleAppFramework;
using Devlooped;

await ConsoleApp.RunAsync(args, RunAsync);

/// <summary>
/// Runs a file-based .NET app from a .cs entrypoint.
/// </summary>
/// <param name="input">Path to an existing .cs file.</param>
/// <param name="extraArgs">Arguments before '--' are passed to 'dotnet publish'; arguments after '--' are forwarded to the published app. Without '--', all extra arguments are forwarded to the published app.
/// </param>
static async Task<int> RunAsync([Argument] string input, [Argument] params string[] extraArgs)
{
    var dotnet = DotnetMuxer.Path?.FullName;
    if (dotnet is null)
    {
        ConsoleApp.LogError("dotnet executable not found.");
        return 1;
    }

    var cs = Path.GetFullPath(input);
    if (!File.Exists(cs))
    {
        ConsoleApp.LogError($"File not found: {cs}");
        return 1;
    }

    var (publishArgs, appArgs) = GoArgs.Split(extraArgs);

    var stamp = Path.ChangeExtension(cs, "stamp");
    if (BuildState.TryRead(stamp, out var state) &&
        BuildManager.IsUpToDate(state))
        return await ProcessRunner.RunAsync(state.App, appArgs);

    File.WriteAllText(stamp, string.Empty, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

    var targets = Path.Combine(AppContext.BaseDirectory, "go.targets");
    var exit = await ProcessRunner.PublishAsync(dotnet, cs, stamp, targets, publishArgs);
    if (exit != 0)
        return exit;

    if (!BuildState.TryRead(stamp, out state))
    {
        ConsoleApp.LogError($"Published app not found.");
        return 1;
    }

    return await ProcessRunner.RunAsync(state.App, appArgs);
}