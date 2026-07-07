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
            var name = Path.GetFileName(directory);
            if (name.Contains('.'))
            {
                // Remote host container (e.g. github.com, gist.github.com). Descend to clean
                // individual ref root (unzipped download) directories by their mtime.
                CleanupRemoteDownloadLocations(directory, cutoff);
                // Prune empty host container if all refs under it were removed.
                try
                {
                    if (!Directory.EnumerateFileSystemEntries(directory).Any())
                        Directory.Delete(directory);
                }
                catch { }
                continue;
            }

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

    static void CleanupRemoteDownloadLocations(string hostDir, DateTime cutoff)
    {
        // Find leaf-ish ref bundle roots: directories containing top-level .cs files or a go.toml
        // (consolidated settings for entry + etag). We only consider their own LastWriteTimeUtc
        // (touched on use of any file in the bundle).
        foreach (var dir in EnumeratePotentialBundleDirs(hostDir))
        {
            try
            {
                if (!Directory.Exists(dir))
                    continue;
                if (Directory.GetLastWriteTimeUtc(dir) >= cutoff)
                    continue;

                // Confirm it looks like one of our download bundles
                if (Directory.EnumerateFiles(dir, "*.cs", SearchOption.TopDirectoryOnly).Any() ||
                    File.Exists(Path.Combine(dir, "go.toml")))
                {
                    Directory.Delete(dir, recursive: true);
                }
            }
            catch
            {
                // Best-effort per location.
            }
        }
    }

    static IEnumerable<string> EnumeratePotentialBundleDirs(string start)
    {
        var stack = new Stack<string>();
        stack.Push(start);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            // Yield current if it may be a bundle root (we'll filter by contents at call site).
            yield return current;
            try
            {
                foreach (var sub in Directory.EnumerateDirectories(current))
                    stack.Push(sub);
            }
            catch { }
        }
    }
}