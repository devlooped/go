using System.Net.Http;
using ConsoleAppFramework;

namespace Devlooped;

/// <summary>
/// Resolves first arg: local file via File.Exists or remote ref via TryParse + download.
/// Remote downloads are always revalidated via ETag against the source (conditional GET).
/// The download bundle root directory mtime is touched on use for periodic cleanup.
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

        // For refs without explicit path, use persisted chosen entry from go.toml (written after first download+pick)
        // for stable publish dir hash and future candidate.
        var settings = RemoteSettingsStore.Load(remote.TempPath);
        if (!string.IsNullOrEmpty(settings.Entry))
            return Path.Combine(remote.TempPath, settings.Entry.Replace('/', Path.DirectorySeparatorChar));

        // Robust discovery (handles case where ExtractToAsync deleted the dir + settings on re-dl, or entry missing).
        // Must prefer 'program.cs' (when present) exactly like the post-extract logic in DownloadIfNeededAsync,
        // otherwise entry-absent + 2+ top-level .cs files yields non-deterministic/wrong effectiveCs.
        if (Directory.Exists(remote.TempPath))
        {
            var prog = Path.Combine(remote.TempPath, "program.cs");
            var first = Directory.EnumerateFiles(remote.TempPath, "*.cs", SearchOption.TopDirectoryOnly).FirstOrDefault();
            var chosen = File.Exists(prog) ? prog : (first ?? prog);
            if (chosen != null)
            {
                try
                {
                    settings.Entry = Path.GetFileName(chosen);
                    RemoteSettingsStore.Save(settings, remote.TempPath);
                }
                catch { }
                return chosen;
            }
        }

        return Path.Combine(remote.TempPath, "program.cs");
    }

    /// <summary>
    /// Pure helper retained for tests: reports whether a file's mtime is older than 14 days.
    /// Note: this is no longer used to gate remote downloads (ETag revalidation is always performed).
    /// </summary>
    public static bool IsRemoteDownloadStale(string sourcePath)
    {
        if (!File.Exists(sourcePath))
            return true;
        var mtime = File.GetLastWriteTimeUtc(sourcePath);
        return (DateTime.UtcNow - mtime) > TimeSpan.FromDays(14);
    }

    public static async Task<string?> GetEffectiveSourceAsync(string input)
    {
        var full = Path.GetFullPath(input);
        if (File.Exists(full))
            return full;

        if (!RemoteRef.TryParse(input, out var remote))
        {
            ConsoleApp.LogError($"File not found: {full}");
            return null;
        }

        return await DownloadIfNeededAsync(remote);
    }

    internal static async Task<string> DownloadIfNeededAsync(RemoteRef remote)
    {
        var candidate = GetRemoteEntryPointPath(remote);

        // Always revalidate remote refs via ETag (conditional request to source).
        // 304 => keep local, 200 => (re)extract. No local mtime-based freshness gate.
        string? etag = null;
        if (Directory.Exists(remote.TempPath))
        {
            var rs = RemoteSettingsStore.Load(remote.TempPath);
            var key = GetStartupKey(remote, candidate);
            etag = RemoteSettingsStore.GetETag(rs, key);
        }

        var provider = DownloadProvider.Create(remote);
        var requestRef = remote with { ETag = etag };
        HttpResponseMessage? contents = null;
        bool didExtract = false;
        string? newEtag = null;
        try
        {
            contents = await provider.GetAsync(requestRef);
            var status = contents.StatusCode;
            var success = contents.IsSuccessStatusCode || status == System.Net.HttpStatusCode.NotModified;
            if (!success)
            {
                ConsoleApp.LogError($"Reference {remote} not found ({status}).");
                // fall through, may fail later if no file
            }
            else if (status == System.Net.HttpStatusCode.NotModified)
            {
                // Fresh per server. Touch only the bundle root dir (for cleanup tracking).
                Directory.Touch(remote.TempPath);
            }
            else
            {
                await contents.ExtractToAsync(remote);
                didExtract = true;

                // Capture ETag; persist below using the *final* candidate (important for
                // no-:path refs where default "program.cs" candidate may be overridden by discovery).
                newEtag = contents.Headers.ETag?.ToString();

                // Mark the ref root (unzipped bundle) dir for cleanup tracking.
                Directory.Touch(remote.TempPath);

                // Touch only the source .cs files after a content refresh so that
                // BuildManager.IsUpToDate sees newer inputs vs prior artifact and rebuilds.
                // (Root dir mtime is the signal used for cleanup of the download location.)
                TouchCsFilesForBuild(remote.TempPath);
            }
        }
        finally
        {
            contents?.Dispose();
        }

        if (didExtract)
        {
            // After extract (which deleted the dir), (re)determine entry and persist it in go.toml.
            if (remote.Path is null)
            {
                var prog = Path.Combine(remote.TempPath, "program.cs");
                var chosen = File.Exists(prog)
                    ? prog
                    : (Directory.EnumerateFiles(remote.TempPath, "*.cs", SearchOption.TopDirectoryOnly).FirstOrDefault() ?? prog);
                candidate = chosen;
                try
                {
                    var rs = RemoteSettingsStore.Load(remote.TempPath);
                    rs.Entry = Path.GetFileName(candidate);
                    RemoteSettingsStore.Save(rs, remote.TempPath);
                }
                catch { }
            }
        }

        // Persist ETag for future conditional requests (if server provided one), using final candidate key.
        if (didExtract && !string.IsNullOrWhiteSpace(newEtag))
        {
            try
            {
                var rs = RemoteSettingsStore.Load(remote.TempPath);
                var key = GetStartupKey(remote, candidate);
                RemoteSettingsStore.SetETag(rs, key, newEtag);
                RemoteSettingsStore.Save(rs, remote.TempPath);
            }
            catch { }
        }

        // If candidate missing but dir now exists (e.g. 304 path or prior state), recompute.
        if (!File.Exists(candidate) && Directory.Exists(remote.TempPath))
        {
            candidate = GetRemoteEntryPointPath(remote);
        }

        return candidate;
    }

    static string GetStartupKey(RemoteRef remote, string candidate)
    {
        if (!string.IsNullOrEmpty(remote.Path))
            return remote.Path.Replace('\\', '/');

        // Use relative path within the bundle root (for no explicit :path, this will be the top-level chosen file e.g. "program.cs")
        try
        {
            var rel = Path.GetRelativePath(remote.TempPath, candidate).Replace('\\', '/');
            return rel;
        }
        catch
        {
            return Path.GetFileName(candidate);
        }
    }

    static void TouchCsFilesForBuild(string directory)
    {
        if (!Directory.Exists(directory))
            return;
        try
        {
            var now = DateTime.UtcNow;
            foreach (var file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
                File.SetLastWriteTimeUtc(file, now);
        }
        catch { }
    }
}
