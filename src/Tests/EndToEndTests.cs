using System.Diagnostics;
using Devlooped;

namespace Tests;

public class EndToEndTests
{
    [Fact]
    public void Dev_second_run_hits_cache_and_edit_invalidates()
    {
        var marker = "e2e-" + Guid.NewGuid().ToString("N");
        var app = CreateApp(marker);

        try
        {
            var (exit1, output1) = RunGo("dev", app);
            Assert.Equal(0, exit1);
            Assert.Contains(marker, output1);

            var stamp = FindStamp(app);
            Assert.True(File.Exists(stamp));
            var stampContent = File.ReadAllText(stamp);
            var stampWriteTime = File.GetLastWriteTimeUtc(stamp);

            var (exit2, output2) = RunGo("dev", app);
            Assert.Equal(0, exit2);
            Assert.Contains(marker, output2);
            Assert.Equal(stampContent, File.ReadAllText(stamp));
            Assert.Equal(stampWriteTime, File.GetLastWriteTimeUtc(stamp));

            var marker2 = "e2e-" + Guid.NewGuid().ToString("N");
            File.WriteAllText(app, $"""
                #:property TargetFramework=net10.0

                Console.WriteLine("{marker2}");
                """);
            File.SetLastWriteTimeUtc(app, DateTime.UtcNow.AddSeconds(2));

            var (exit3, output3) = RunGo("dev", app);
            Assert.Equal(0, exit3);
            Assert.Contains(marker2, output3);
            Assert.DoesNotContain(marker, output3);
            Assert.NotEqual(stampContent, File.ReadAllText(stamp));
        }
        finally
        {
            CleanApp(app);
        }
    }

    [Fact]
    public void Publish_r2r_second_run_hits_cache()
    {
        var marker = "e2e-" + Guid.NewGuid().ToString("N");
        var app = CreateApp(marker);

        try
        {
            var (exit1, output1) = RunGo(app, "--r2r");
            Assert.Equal(0, exit1);
            Assert.Contains(marker, output1);

            var stamp = FindStamp(app);
            var stampContent = File.ReadAllText(stamp);
            Assert.Contains("app=", stampContent);
            Assert.Contains("mode=r2r", stampContent);
            var stampWriteTime = File.GetLastWriteTimeUtc(stamp);

            var (exit2, output2) = RunGo(app, "--r2r");
            Assert.Equal(0, exit2);
            Assert.Contains(marker, output2);
            Assert.Equal(stampContent, File.ReadAllText(stamp));
            Assert.Equal(stampWriteTime, File.GetLastWriteTimeUtc(stamp));
        }
        finally
        {
            CleanApp(app);
        }
    }

    static string CreateApp(string marker)
    {
        var dir = CreateTempDir();
        var app = Path.Combine(dir, "app.cs");
        File.WriteAllText(app, $"""
            #:property TargetFramework=net10.0

            Console.WriteLine("{marker}");
            """);
        return app;
    }

    static string GetTempRoot()
    {
        var directory = OperatingSystem.IsWindows()
            ? Path.GetTempPath()
            : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Join(directory, "dotnet", "go");
    }

    static string FindStamp(string appFile)
    {
        var normalizedInput = $"input={appFile.Replace('\\', '/')}";
        var prefix = Path.GetFileNameWithoutExtension(appFile) + "-";
        var matches = Directory.GetDirectories(GetTempRoot(), prefix + "*")
            .Where(dir => File.Exists(Path.Combine(dir, "go.stamp")))
            .Where(dir => File.ReadAllText(Path.Combine(dir, "go.stamp")).Contains(normalizedInput))
            .ToArray();

        Assert.Single(matches);
        return Path.Combine(matches[0], "go.stamp");
    }

    static (int ExitCode, string Output) RunGo(params string[] args)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = DotnetMuxer.Path!.FullName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.Environment["NO_COLOR"] = "true";
        startInfo.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "go.dll"));
        foreach (var arg in args)
            startInfo.ArgumentList.Add(arg);

        using var process = Process.Start(startInfo)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        Task.WaitAll(stdout, stderr);

        return (process.ExitCode, stdout.Result + stderr.Result);
    }

    static void CleanApp(string appFile)
    {
        try
        {
            RunGo("clean", appFile);
        }
        catch
        {
        }

        try
        {
            var dir = Path.GetDirectoryName(appFile);
            if (dir is not null && Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch
        {
        }
    }

    static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "go-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}