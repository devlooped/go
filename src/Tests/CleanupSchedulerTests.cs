using Devlooped;

namespace Tests;

public class CleanupSchedulerTests
{
    [Fact]
    public void ShouldRun_returns_true_when_last_cleanup_is_null()
    {
        var settingsPath = CreateSettingsPath();

        try
        {
            SettingsStore.Save(new Settings(), settingsPath);

            Assert.True(CleanupScheduler.ShouldRun(settingsPath: settingsPath));
        }
        finally
        {
            DeleteIfExists(settingsPath);
        }
    }

    [Fact]
    public void ShouldRun_returns_true_when_last_cleanup_is_older_than_default_days()
    {
        var settingsPath = CreateSettingsPath();

        try
        {
            SettingsStore.Save(new Settings
            {
                LastCleanupUtc = DateTimeOffset.UtcNow.AddDays(-3),
            }, settingsPath);

            Assert.True(CleanupScheduler.ShouldRun(settingsPath: settingsPath));
        }
        finally
        {
            DeleteIfExists(settingsPath);
        }
    }

    [Fact]
    public void ShouldRun_returns_false_when_last_cleanup_is_recent()
    {
        var settingsPath = CreateSettingsPath();

        try
        {
            SettingsStore.Save(new Settings
            {
                LastCleanupUtc = DateTimeOffset.UtcNow.AddHours(-1),
            }, settingsPath);

            Assert.False(CleanupScheduler.ShouldRun(settingsPath: settingsPath));
        }
        finally
        {
            DeleteIfExists(settingsPath);
        }
    }

    static string CreateSettingsPath()
    {
        var dir = Path.Combine(Path.GetTempPath(), "go-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "go.toml");
    }

    static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }
}