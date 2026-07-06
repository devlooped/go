using System.Diagnostics;

namespace Devlooped;

public static class ProcessRunner
{
    public static Task<int> PublishAsync(string dotnet, string cs, string config, string targets, IReadOnlyList<string>? publishArgs = null)
    {
        var environment = new Dictionary<string, string>
        {
            ["GoConfig"] = config,
            ["CustomAfterMicrosoftCSharpTargets"] = targets,
        };

        var arguments = new List<string> { "publish", cs };
        if (publishArgs is not null)
            arguments.AddRange(publishArgs);

        return RunAsync(dotnet, arguments, environment);
    }

    public static Task<int> RunAsync(string appPath, IReadOnlyList<string>? extraArgs = null)
        => RunAsync(appPath, extraArgs, environment: null);

    public static async Task<int> RunAsync(string fileName, IReadOnlyList<string>? arguments, IReadOnlyDictionary<string, string>? environment)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
        };

        if (arguments is not null)
        {
            foreach (var arg in arguments)
                startInfo.ArgumentList.Add(arg);
        }

        if (environment is not null)
        {
            foreach (var (key, value) in environment)
                startInfo.Environment[key] = value;
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start process: {fileName}");

        await process.WaitForExitAsync();

        return process.ExitCode;
    }
}