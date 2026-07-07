namespace Devlooped;

public static class CleanupManager
{
    public const int DefaultDays = 2;

    public static int Cleanup(int days = DefaultDays, string? root = null, string? settingsPath = null)
    {
        CleanupStaleDirectories(root ?? Directory.GetTempRoot(), days);

        SettingsStore.Save(new Settings { LastCleanupUtc = DateTimeOffset.UtcNow }, settingsPath);
        return 0;
    }

    public static void CleanupStaleDirectories(string root, int days)
    {
        var cutoff = DateTime.UtcNow.AddDays(-days);

        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            if (Directory.GetLastWriteTimeUtc(directory) >= cutoff)
                continue;

            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch
            {
                // Best-effort: locked/undeletable directories are retried on the next sweep.
            }
        }
    }
}