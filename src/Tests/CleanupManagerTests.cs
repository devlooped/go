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
    public void CleanupStaleDirectories_continues_when_a_directory_cannot_be_deleted()
    {
        var root = CreateTempDir();
        var locked = CreateSubDir(root, "locked");
        var deletable = CreateSubDir(root, "deletable");

        Directory.SetLastWriteTimeUtc(locked, DateTime.UtcNow.AddDays(-5));
        Directory.SetLastWriteTimeUtc(deletable, DateTime.UtcNow.AddDays(-5));

        using var blocker = BlockDirectoryDeletion(locked);

        CleanupManager.CleanupStaleDirectories(root, days: 2);

        Assert.True(Directory.Exists(locked));
        Assert.False(Directory.Exists(deletable));
    }

    [Fact]
    public void Cleanup_saves_settings_even_when_a_delete_fails()
    {
        var root = CreateTempDir();
        var settingsRoot = CreateTempDir();
        var settingsPath = Path.Combine(settingsRoot, "go.toml");

        var locked = CreateSubDir(root, "locked");
        Directory.SetLastWriteTimeUtc(locked, DateTime.UtcNow.AddDays(-5));

        using var blocker = BlockDirectoryDeletion(locked);

        var exit = CleanupManager.Cleanup(days: 2, root: root, settingsPath: settingsPath);

        Assert.Equal(0, exit);
        Assert.NotNull(SettingsStore.Load(settingsPath).LastCleanupUtc);
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

    static IDisposable BlockDirectoryDeletion(string directory)
    {
        var lockedFile = Path.Combine(directory, "file.txt");
        File.WriteAllText(lockedFile, "locked");
        return new FileStream(lockedFile, FileMode.Open, FileAccess.Read, FileShare.None);
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