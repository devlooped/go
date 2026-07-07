using System.Net.Http;
using ConsoleAppFramework;

namespace Devlooped;

/// <summary>
/// Resolves first arg: local file via File.Exists or remote ref via TryParse + conditional download.
/// Download uses 2-week mtime window for staleness.
/// </summary>
public static class RemoteSourceResolver
{
    public static bool TryResolveEntryPoint(string input, out string effectiveCs, out string publishDir, out string stamp)
    {
        var full = Path.GetFullPath(input);
        if (File.Exists(full))
        {
            effectiveCs = full;
            publishDir = Directory.GetPublishDir(effectiveCs);
            stamp = Path.Combine(publishDir, "go.stamp");
            return true;
        }

        if (RemoteRef.TryParse(input, out var remote))
        {
            effectiveCs = GetRemoteEntryPointPath(remote);
            publishDir = Directory.GetPublishDir(effectiveCs);
            stamp = Path.Combine(publishDir, "go.stamp");
            return true;
        }

        ConsoleApp.LogError($"File not found: {full}");
        effectiveCs = string.Empty;
        publishDir = string.Empty;
        stamp = string.Empty;
        return false;
    }

    public static string GetRemoteEntryPointPath(RemoteRef remote)
    {
        if (remote.Path != null)
            return Path.Combine(remote.TempPath, remote.Path.Replace('/', Path.DirectorySeparatorChar));

        // For refs without explicit path, use persisted chosen entry (written after first download+pick) for stable publish dir hash and future candidate.
        var marker = Path.Combine(remote.TempPath, ".go-entry");
        if (File.Exists(marker))
        {
            var name = File.ReadAllText(marker).Trim();
            if (!string.IsNullOrEmpty(name))
                return Path.Combine(remote.TempPath, name.Replace('/', Path.DirectorySeparatorChar));
        }

        // Robust discovery (handles case where ExtractToAsync deleted the dir + marker on re-dl, or marker missing for other reason):
        // If files are already present on disk, pick the first .cs (so we don't resolve to non-existent "program.cs" and force spurious dl).
        if (Directory.Exists(remote.TempPath))
        {
            var first = Directory.EnumerateFiles(remote.TempPath, "*.cs", SearchOption.TopDirectoryOnly).FirstOrDefault();
            if (first != null)
            {
                var name = Path.GetFileName(first);
                try { File.WriteAllText(marker, name); } catch { }
                return first;
            }
        }

        return Path.Combine(remote.TempPath, "program.cs");
    }

    public static bool IsRemoteDownloadStale(string sourcePath)
    {
        if (!File.Exists(sourcePath))
            return true;
        var mtime = File.GetLastWriteTimeUtc(sourcePath);
        return (DateTime.UtcNow - mtime) > TimeSpan.FromDays(14);
    }

    public static async Task<string?> GetEffectiveSourceAsync(string input, bool force)
    {
        var full = Path.GetFullPath(input);
        if (File.Exists(full))
            return full;

        if (!RemoteRef.TryParse(input, out var remote))
        {
            ConsoleApp.LogError($"File not found: {full}");
            return null;
        }

        return await DownloadIfNeededAsync(remote, force);
    }

    internal static async Task<string> DownloadIfNeededAsync(RemoteRef remote, bool force)
    {
        var candidate = GetRemoteEntryPointPath(remote);
        bool needsDownload = force || !File.Exists(candidate) || IsRemoteDownloadStale(candidate);

        if (needsDownload)
        {
            var provider = DownloadProvider.Create(remote);
            HttpResponseMessage? contents = null;
            try
            {
                contents = await provider.GetAsync(remote);
                var success = contents.IsSuccessStatusCode || contents.StatusCode == System.Net.HttpStatusCode.NotModified;
                if (!success)
                {
                    ConsoleApp.LogError($"Reference {remote} not found ({contents.StatusCode}).");
                    // fall through, may fail later if no file
                }
                else if (contents.StatusCode != System.Net.HttpStatusCode.NotModified)
                {
                    await contents.ExtractToAsync(remote);
                    // Update mtimes on all extracted files to now (participates in 2w window + build uptodate)
                    TouchAllFiles(remote.TempPath);
                }
            }
            finally
            {
                contents?.Dispose();
            }

            // After (possible) extract on a download path, (re)determine the actual entry point and (re)write the .go-entry marker.
            // This guarantees the marker survives ExtractToAsync's Directory.Delete of the entire TempPath.
            // We no longer rely solely on the pre-dl 'candidate' value for the existence check.
            if (remote.Path is null)
            {
                var prog = Path.Combine(remote.TempPath, "program.cs");
                var chosen = File.Exists(prog)
                    ? prog
                    : (Directory.EnumerateFiles(remote.TempPath, "*.cs", SearchOption.TopDirectoryOnly).FirstOrDefault() ?? prog);
                candidate = chosen;
                try
                {
                    File.WriteAllText(Path.Combine(remote.TempPath, ".go-entry"), Path.GetFileName(candidate));
                }
                catch { }
            }

            // Ensure the chosen source (and other .cs) have fresh mtime for the window and for stamp/build logic
            if (File.Exists(candidate))
                File.SetLastWriteTimeUtc(candidate, DateTime.UtcNow);
            foreach (var csFile in Directory.EnumerateFiles(remote.TempPath, "*.cs", SearchOption.AllDirectories))
                File.SetLastWriteTimeUtc(csFile, DateTime.UtcNow);
        }

        return candidate;
    }

    static void TouchAllFiles(string directory)
    {
        if (!Directory.Exists(directory))
            return;
        try
        {
            var now = DateTime.UtcNow;
            foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
                File.SetLastWriteTimeUtc(file, now);
        }
        catch { }
    }
}
