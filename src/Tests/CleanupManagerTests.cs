using Devlooped;

namespace Tests;

public class CleanupManagerTests
{
    [Fact]
    public void CleanupStaleDirectories_deletes_directories_older_than_cutoff()
    {
        var root = CreateTempDir();
        var stale = CreateSubDir(root, "stale");
        var fresh = CreateSubDir(root, "fresh");

        Directory.SetLastWriteTimeUtc(stale, DateTime.UtcNow.AddDays(-5));
        Directory.SetLastWriteTimeUtc(fresh, DateTime.UtcNow);

        CleanupManager.CleanupStaleDirectories(root, days: 2);

        Assert.False(Directory.Exists(stale));
        Assert.True(Directory.Exists(fresh));
    }

    [Fact]
    public void SettingsStore_roundtrips_last_cleanup_utc()
    {
        var root = CreateTempDir();
        var settingsPath = Path.Combine(root, "go.toml");

        try
        {
            var expected = new DateTimeOffset(2026, 3, 15, 12, 30, 0, TimeSpan.Zero);
            SettingsStore.Save(new Settings { LastCleanupUtc = expected }, settingsPath);

            var loaded = SettingsStore.Load(settingsPath);

            Assert.Equal(expected, loaded.LastCleanupUtc);
        }
        finally
        {
            if (File.Exists(settingsPath))
                File.Delete(settingsPath);
        }
    }

    static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "go-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    static string CreateSubDir(string root, string name)
    {
        var path = Path.Combine(root, name);
        Directory.CreateDirectory(path);
        return path;
    }
}