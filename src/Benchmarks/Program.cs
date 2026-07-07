using System.Diagnostics;
using System.Runtime.InteropServices;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;

var artifactsDir = Path.GetFullPath(Path.Combine(
    ThisAssembly.Project.MSBuildProjectDirectory, "..", "..", "artifacts"));

var config = DefaultConfig.Instance
    .WithArtifactsPath(artifactsDir);

BenchmarkRunner.Run<Benchmarks>(config);

public record Sample(string Name, string App)
{
    public override string ToString() => Name;
}

[Config(typeof(BenchmarkConfig))]
public class Benchmarks
{
    class BenchmarkConfig : ManualConfig
    {
        public BenchmarkConfig()
        {
            // Use fewer iterations because these are high-level process invocations (hundreds of ms to seconds)
            AddJob(Job.Default
                .WithLaunchCount(1)
                .WithWarmupCount(1)
                .WithIterationCount(3)
                .WithInvocationCount(1)
                .WithUnrollFactor(1));
        }
    }

    string _repoRoot = null!;
    const string _dotnet = "dotnet";

    [GlobalSetup]
    public void GlobalSetup()
    {
        _repoRoot = FindRepoRoot();

        // Require that `dotnet go` works (the tool must be installed in a way that `dotnet go --version` succeeds).
        // This avoids any dnx overhead in the measurements.
        var (exit, stdout, stderr) = RunProcessCapture(_dotnet, ["go", "--version"]);
        if (exit != 0)
        {
            throw new InvalidOperationException(
                $"`dotnet go --version` must succeed (exit {exit}).\n" +
                $"stdout: {stdout}\nstderr: {stderr}\n" +
                "Install the 'go' tool (e.g. from local package) so that `dotnet go` is available.");
        }

        Console.WriteLine($"[setup] dotnet go --version: {stdout.Trim()}");
        Console.WriteLine($"[setup] samples root: {_repoRoot}");
    }

    static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir) && dir.Length > 3)
        {
            if (File.Exists(Path.Combine(dir, "go.slnx")))
                return dir;
            // Also accept if we see the samples folder as we walk up
            if (Directory.Exists(Path.Combine(dir, "samples")) && File.Exists(Path.Combine(dir, "readme.md")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }

        // Fallback: assume running from inside the repo tree
        var fallback = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        return fallback;
    }

    [ParamsSource(nameof(GetSamples))]
    public Sample Sample { get; set; } = default!;

    public IEnumerable<Sample> GetSamples()
    {
        var root = _repoRoot ?? FindRepoRoot();
        var samplesDir = Path.Combine(root, "samples");
        if (!Directory.Exists(samplesDir))
            yield break;

        foreach (var full in Directory.GetFiles(samplesDir, "app.cs", SearchOption.AllDirectories).OrderBy(p => p))
        {
            var name = Path.GetFileName(Path.GetDirectoryName(full))!;
            yield return new Sample(name, full);
        }
    }

    string GetFullSamplePath() => Sample.App;

    string GetSampleDirectory() => Path.GetDirectoryName(GetFullSamplePath())!;

    string GetPublishedExePath()
    {
        var dir = GetSampleDirectory();
        var baseName = Path.Combine(dir, "artifacts", "app", "app");
        return RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? baseName + ".exe" : baseName;
    }

    [Benchmark(Description = "dnx go")]
    public void DnxGo()
    {
        var (exit, _, err) = RunProcessCapture(_dotnet, ["go", GetFullSamplePath()]);
        if (exit != 0) throw new Exception($"dnx go failed: {err}");
    }

    [Benchmark(Description = "dotnet publish")]
    public void DotnetPublish()
    {
        // Run publish (this measures the full publish cost + run of resulting exe)
        var sample = GetFullSamplePath();
        var (pubExit, _, pubErr) = RunProcessCapture(_dotnet, ["publish", sample, "--nologo", "-v", "q"]);
        if (pubExit != 0) throw new Exception($"dotnet publish failed: {pubErr}");

        var exe = GetPublishedExePath();
        if (!File.Exists(exe))
            throw new FileNotFoundException($"Expected published executable not found at {exe}");

        var (runExit, _, runErr) = RunProcessCapture(exe, []);
        if (runExit != 0) throw new Exception($"published exe failed: {runErr}");
    }

    [Benchmark(Description = "dnx go dev")]
    public void DnxGoDev()
    {
        var (exit, _, err) = RunProcessCapture(_dotnet, ["go", "dev", GetFullSamplePath()]);
        if (exit != 0) throw new Exception($"dnx go dev failed: {err}");
    }

    [Benchmark(Description = "dotnet run")]
    public void DotnetRun()
    {
        var (exit, _, err) = RunProcessCapture(_dotnet, ["run", GetFullSamplePath()]);
        if (exit != 0) throw new Exception($"dotnet run failed: {err}");
    }

    static (int ExitCode, string StdOut, string StdErr) RunProcessCapture(string fileName, string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var a in args)
            psi.ArgumentList.Add(a);

        using var process = Process.Start(psi) ?? throw new InvalidOperationException($"Could not start process: {fileName}");

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return (process.ExitCode, stdout, stderr);
    }
}