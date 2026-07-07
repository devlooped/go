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
        Assert.True(Directory.Exists(root));
        Assert.False(File.Exists(stamp));
    }

    [Fact]
    public void Clean_succeeds_when_publish_directory_is_missing()
    {
        var root = Path.Combine(Path.GetTempPath(), "go-tests-" + Guid.NewGuid().ToString("N"));
        var stamp = Path.Combine(root, "go.stamp");

        var exit = AppCleaner.Clean(root, stamp);

        Assert.Equal(0, exit);
    }

    static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "go-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}