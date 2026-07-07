using Devlooped;

namespace Tests;

public class AppCleanerTests
{
    [Fact]
    public void Clean_deletes_publish_directory_when_present()
    {
        var root = CreateTempDir();
        var stamp = Path.Combine(root, "go.stamp");
        File.WriteAllText(stamp, "mode=aot");
        File.WriteAllText(Path.Combine(root, "app.exe"), "app");

        var exit = AppCleaner.Clean(root, stamp);

        Assert.Equal(0, exit);
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public void Clean_deletes_stamp_when_publish_directory_delete_fails()
    {
        var root = CreateTempDir();
        var stamp = Path.Combine(root, "go.stamp");
        var locked = Path.Combine(root, "locked.exe");
        File.WriteAllText(stamp, "mode=aot");
        File.WriteAllText(locked, "locked");

        using var stream = new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None);

        var exit = AppCleaner.Clean(root, stamp);

        Assert.Equal(0, exit);
        if (OperatingSystem.IsWindows())
        {
            // Exclusive lock should cause Directory.Delete to fail, triggering stamp-only fallback
            Assert.True(Directory.Exists(root));
            Assert.False(File.Exists(stamp));
        }
        else
        {
            // On Unix, open file locks do not prevent directory deletion; dir (and stamp) are removed
            Assert.False(Directory.Exists(root));
        }
    }

    [Fact]
    public void Clean_succeeds_when_publish_directory_is_missing()
    {
        var root = Path.Combine(Path.GetTempPath(), "go-tests-" + Guid.NewGuid().ToString("N"));
        var stamp = Path.Combine(root, "go.stamp");

        var exit = AppCleaner.Clean(root, stamp);

        Assert.Equal(0, exit);
    }

    [Fact]
    public async Task Clean_runs_dotnet_clean_for_entry_point_before_removing_publish_dir()
    {
        var dotnet = DotnetMuxer.Path?.FullName;
        if (dotnet is null)
            return;

        var dir = CreateTempDir();
        var cs = Path.GetFullPath(Path.Combine(dir, "app.cs"));
        File.WriteAllText(cs, """
            #:property TargetFramework=net10.0

            Console.WriteLine("clean-test");
            """);

        var exit = await ProcessRunner.RunAsync(dotnet, ["run", cs, "-v:q"], environment: null);
        Assert.Equal(0, exit);

        var publishDir = Path.Combine(dir, "publish");
        var stamp = Path.Combine(publishDir, "go.stamp");
        Directory.CreateDirectory(publishDir);
        File.WriteAllText(stamp, string.Empty);

        // File-based apps use the same entry-point hash for dotnet/go and dotnet/runfile caches.
        var cacheDirName = Path.GetFileName(Directory.GetPublishDir(cs));
        var runfileRoot = Path.Combine(Path.GetTempPath(), "dotnet", "runfile", cacheDirName);
        Assert.True(Directory.Exists(runfileRoot));
        var builtApp = Path.Combine(runfileRoot, "bin", "debug", "app.dll");
        Assert.True(File.Exists(builtApp));

        exit = AppCleaner.Clean(publishDir, stamp, cs);

        Assert.Equal(0, exit);
        Assert.False(File.Exists(builtApp));

        try { Directory.Delete(dir, recursive: true); } catch { }
    }

    static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "go-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}