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

    [Fact]
    public void Remote_ref_default_command_runs_twice_identical_and_uses_dotnet_go()
    {
        var scratch = @"C:\Users\kzu\AppData\Local\Temp\grok-goal-4376de8fe197\implementer";
        Directory.CreateDirectory(scratch);
        var remoteRef = "gist.github.com/kzu/0ac826dc7de666546aaedd38e5965381";
        try
        {
            // warm-up populate (may emit build logs)
            RunGo(remoteRef, "--", "-v:q");

            // two runs that should hit cache for identical clean output
            var (exit1, output1) = RunGo(remoteRef, "--", "-v:q");
            File.WriteAllText(Path.Combine(scratch, "remote-run1.log"), output1);
            Assert.Equal(0, exit1);
            Assert.Contains("run.cs", output1);

            var (exit2, output2) = RunGo(remoteRef, "--", "-v:q");
            File.WriteAllText(Path.Combine(scratch, "remote-run2.log"), output2);
            Assert.Equal(0, exit2);
            Assert.Contains("run.cs", output2);
            Assert.Equal(output1, output2);

            // basedir evidence
            var root = GetTempRoot();
            var listing = string.Join(Environment.NewLine, Directory.GetDirectories(root, "*", SearchOption.AllDirectories).Take(10).Select(d => d.Replace('\\', '/')));
            File.WriteAllText(Path.Combine(scratch, "basedir.log"), listing);
            Assert.Contains("/dotnet/go/", listing);
        }
        finally
        {
            try { RunGo("clean", remoteRef); } catch { }
        }
    }

    [Fact]
    public void Remote_ref_dev_command_runs_twice_identical_and_uses_dotnet_go()
    {
        var scratch = @"C:\Users\kzu\AppData\Local\Temp\grok-goal-4376de8fe197\implementer";
        Directory.CreateDirectory(scratch);
        var remoteRef = "gist.github.com/kzu/0ac826dc7de666546aaedd38e5965381";
        try
        {
            // warm-up
            RunGo("dev", remoteRef, "--", "-v:q");

            var (exit1, output1) = RunGo("dev", remoteRef, "--", "-v:q");
            File.WriteAllText(Path.Combine(scratch, "remote-dev1.log"), output1);
            Assert.Equal(0, exit1);
            Assert.Contains("run.cs", output1);

            var (exit2, output2) = RunGo("dev", remoteRef, "--", "-v:q");
            File.WriteAllText(Path.Combine(scratch, "remote-dev2.log"), output2);
            Assert.Equal(0, exit2);
            Assert.Contains("run.cs", output2);
            Assert.Equal(output1, output2);
        }
        finally
        {
            try { RunGo("clean", remoteRef); } catch { }
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

    [Fact]
    public void Remote_stale_2week_re_download_refreshes_mtime_immediate_followup_does_not()
    {
        var scratch = @"C:\Users\kzu\AppData\Local\Temp\grok-goal-4376de8fe197\implementer";
        Directory.CreateDirectory(scratch);
        var remoteRef = "gist.github.com/kzu/0ac826dc7de666546aaedd38e5965381";
        var logPath = Path.Combine(scratch, "stale-download.log");

        try
        {
            // Ensure clean-ish start for this ref's cache dir (best effort)
            try { RunGo("clean", remoteRef); } catch { }

            // Warm-up download
            var warm = RunGo(remoteRef, "--", "-v:q");
            Assert.Equal(0, warm.ExitCode);
            Assert.Contains("run.cs", warm.Output);

            // Locate the downloaded source cs for this ref (robust search, not brittle FirstOrDefault + loose contains)
            var goRoot = GetTempRoot();
            var gistCandidates = Directory.GetDirectories(goRoot, "*0ac826dc7de666546aaedd38e5965381*", SearchOption.AllDirectories)
                .OrderByDescending(d => d.Length)
                .ToArray();
            var gistRoot = gistCandidates.FirstOrDefault(d => Directory.GetFiles(d, "*.cs", SearchOption.AllDirectories).Any())
                ?? gistCandidates.FirstOrDefault();
            Assert.False(string.IsNullOrEmpty(gistRoot), "expected download dir for gist ref");
            var sourceCs = Directory.GetFiles(gistRoot, "run.cs", SearchOption.AllDirectories).FirstOrDefault()
                ?? Directory.GetFiles(gistRoot, "*.cs", SearchOption.AllDirectories).OrderBy(f => f).FirstOrDefault();
            Assert.False(string.IsNullOrEmpty(sourceCs), "expected source .cs under download");
            var srcInfo = new FileInfo(sourceCs);

            // Artificially age it >14 days
            var staleTime = DateTime.UtcNow.AddDays(-20);
            srcInfo.LastWriteTimeUtc = staleTime;
            Assert.True((DateTime.UtcNow - srcInfo.LastWriteTimeUtc).TotalDays > 14);

            // Re-invoke: should detect stale, (re)download/touch mtime to recent, succeed with marker
            var (exitStale, outStale) = RunGo(remoteRef, "--", "-v:q");
            File.WriteAllText(logPath, "STALE-RE-INVOKE:\n" + outStale);
            Assert.Equal(0, exitStale);
            Assert.Contains("run.cs", outStale);

            srcInfo.Refresh();
            var afterStaleMtime = srcInfo.LastWriteTimeUtc;
            File.AppendAllText(logPath, $"\nSOURCE-MTIME-AFTER-STALE: {afterStaleMtime:O}\n");
            Assert.True((DateTime.UtcNow - afterStaleMtime).TotalMinutes < 5, "source mtime should be refreshed to recent by re-dl");

            // Immediate follow-up: must NOT update the source mtime (plan requirement)
            var mtimeBeforeImmediate = srcInfo.LastWriteTimeUtc;
            var (exitImm, outImm) = RunGo(remoteRef, "--", "-v:q");
            File.AppendAllText(logPath, "IMMEDIATE-FOLLOWUP:\n" + outImm);
            Assert.Equal(0, exitImm);
            Assert.Contains("run.cs", outImm);

            srcInfo.Refresh();
            var mtimeAfterImmediate = srcInfo.LastWriteTimeUtc;
            File.AppendAllText(logPath, $"SOURCE-MTIME-AFTER-IMMEDIATE: {mtimeAfterImmediate:O}\n");

            var deltaSeconds = (mtimeAfterImmediate - mtimeBeforeImmediate).TotalSeconds;
            // The immediate run (cache hit, no dl) must not have updated the source mtime via our logic or observable change.
            Assert.True(deltaSeconds < 2.0, $"immediate follow-up must not update source mtime (delta={deltaSeconds}s)");
        }
        finally
        {
            try { RunGo("clean", remoteRef); } catch { }
        }
    }
}