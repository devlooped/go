using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Devlooped;

/// <summary>
/// Probes for the native C/C++ linker toolchain required by native AOT publishes.
/// </summary>
static class NativeToolchain
{
    public const string WindowsFixCommand = "dnx vs -- install --passive --sku:build";
    public const string LinuxFixCommand = "sudo apt-get install -y build-essential";
    public const string MacFixCommand = "xcode-select --install";

    public const string ReadyMessage = "Native toolchain OK for AOT publishes.";
    public const string MissingDotnetMessage = "dotnet executable not found.";
    public const string MissingWindowsMessage = "Visual C++ build tools not found (required for native AOT publishes).";
    public const string MissingLinuxMessage = "Platform linker ('clang' or 'gcc') not found (required for native AOT publishes).";
    public const string MissingMacMessage = "Platform linker (clang / Xcode Command Line Tools) not found (required for native AOT publishes).";

    internal sealed record CheckResult(bool Ok, string Message, string? FixCommand);

    /// <summary>Evaluates whether the current machine can perform native AOT builds.</summary>
    public static CheckResult Evaluate()
        => Evaluate(
            DotnetMuxer.Path?.FullName,
            RuntimeInformation.IsOSPlatform,
            CommandExists,
            RunCaptured);

    /// <summary>Testable entry point with injected dependencies.</summary>
    internal static CheckResult Evaluate(
        string? dotnetPath,
        Func<OSPlatform, bool> isOs,
        Func<string, bool> commandExists,
        Func<string, IReadOnlyList<string>, CapturedProcessResult> run)
    {
        if (string.IsNullOrEmpty(dotnetPath))
            return new CheckResult(false, MissingDotnetMessage, FixCommand: null);

        if (isOs(OSPlatform.Windows))
            return CheckWindows(dotnetPath, commandExists, run);

        if (isOs(OSPlatform.OSX))
            return CheckMacOS(commandExists, run);

        return CheckLinux(commandExists);
    }

    static CheckResult CheckWindows(
        string dotnetPath,
        Func<string, bool> commandExists,
        Func<string, IReadOnlyList<string>, CapturedProcessResult> run)
    {
        // dnx runs the vs tool even when not globally installed.
        var args = new[] { "dnx", "vs", "--", "where", "+vc", "--prop", "InstallationPath" };
        var result = run(dotnetPath, args);
        if (result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.StdOut))
            return new CheckResult(true, ReadyMessage, FixCommand: null);

        // Developer Command Prompt often has cl on PATH even when the vs query fails.
        if (commandExists("cl"))
            return new CheckResult(true, ReadyMessage, FixCommand: null);

        return new CheckResult(false, MissingWindowsMessage, WindowsFixCommand);
    }

    static CheckResult CheckLinux(Func<string, bool> commandExists)
    {
        if (commandExists("clang") || commandExists("gcc"))
            return new CheckResult(true, ReadyMessage, FixCommand: null);

        return new CheckResult(false, MissingLinuxMessage, LinuxFixCommand);
    }

    static CheckResult CheckMacOS(
        Func<string, bool> commandExists,
        Func<string, IReadOnlyList<string>, CapturedProcessResult> run)
    {
        if (commandExists("clang"))
            return new CheckResult(true, ReadyMessage, FixCommand: null);

        try
        {
            var result = run("xcode-select", ["-p"]);
            if (result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.StdOut))
                return new CheckResult(true, ReadyMessage, FixCommand: null);
        }
        catch
        {
            // xcode-select missing or failed — fall through to recommendation.
        }

        return new CheckResult(false, MissingMacMessage, MacFixCommand);
    }

    internal static bool CommandExists(string commandName)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathEnv))
            return false;

        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        var extensions = isWindows
            ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT;.COM")
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [""];

        foreach (var directory in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string dir;
            try
            {
                dir = Path.GetFullPath(directory.Trim('"'));
            }
            catch
            {
                continue;
            }

            if (!Directory.Exists(dir))
                continue;

            if (!isWindows)
            {
                var unixPath = Path.Combine(dir, commandName);
                if (File.Exists(unixPath))
                    return true;
                continue;
            }

            foreach (var ext in extensions)
            {
                var candidate = Path.Combine(dir, commandName + ext);
                if (File.Exists(candidate))
                    return true;
            }

            // Already-extension forms (e.g. cl.exe passed as name).
            var bare = Path.Combine(dir, commandName);
            if (File.Exists(bare))
                return true;
        }

        return false;
    }

    internal sealed record CapturedProcessResult(int ExitCode, string StdOut, string StdErr);

    /// <summary>Runs a process and captures stdout/stderr (separate helper for native checks).</summary>
    internal static CapturedProcessResult RunCaptured(string fileName, IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        foreach (var arg in arguments)
            startInfo.ArgumentList.Add(arg);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start process: {fileName}");

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                stdout.AppendLine(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                stderr.AppendLine(e.Data);
        };

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.WaitForExit();

        return new CapturedProcessResult(process.ExitCode, stdout.ToString().Trim(), stderr.ToString().Trim());
    }
}
