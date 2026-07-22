using System.Diagnostics.CodeAnalysis;

namespace Devlooped;

/// <summary>A previously run go entry from root <c>go.toml</c> history.</summary>
public record RunHistoryEntry(string Input, DateTimeOffset LastUsedUtc, int UseCount)
{
    /// <summary>Display label for the picker (forward slashes for paths; gists as owner/file).</summary>
    public string Display { get; init; } = Input.Replace('\\', '/');
}

/// <summary>
/// Run history persisted in the same root <c>go.toml</c> as cleanup settings.
/// Pure helpers accept an optional settings path for fixture tests.
/// </summary>
public static class RunHistory
{
    /// <summary>
    /// Loads history from <paramref name="settingsPath"/> (default: root go.toml),
    /// ordered most-used first, then most-recently-used, then alphabetically.
    /// Drops non-selectable entries (e.g. deleted local files) from storage so later
    /// lists skip the filesystem checks; saves at most once after pruning.
    /// </summary>
    public static IReadOnlyList<RunHistoryEntry> List(string? settingsPath = null)
    {
        var settings = SettingsStore.Load(settingsPath);
        if (settings.History is not { Count: > 0 })
            return [];

        var pruned = false;
        // BaseDisplay is owner/file for gists when known; Gist* fields support collision disambiguation.
        var pending = new List<(string Input, DateTimeOffset LastUsedUtc, int UseCount, string BaseDisplay, string? GistOwner, string? GistId, string? GistFile)>(settings.History.Count);
        for (var i = settings.History.Count - 1; i >= 0; i--)
        {
            var h = settings.History[i];
            if (string.IsNullOrWhiteSpace(h.Input) || !IsSelectable(h.Input))
            {
                settings.History.RemoveAt(i);
                pruned = true;
                continue;
            }

            var baseDisplay = FormatBaseDisplay(h.Input, h.Entry, out var gistOwner, out var gistId, out var gistFile);
            pending.Add((
                h.Input,
                h.LastUsedUtc == default ? DateTimeOffset.MinValue : h.LastUsedUtc,
                h.UseCount <= 0 ? 1 : h.UseCount,
                baseDisplay,
                gistOwner,
                gistId,
                gistFile));
        }

        if (pruned)
            SettingsStore.Save(settings, settingsPath);

        // Disambiguate gist labels that share the same owner/file with a short gist id.
        var gistLabelCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in pending)
        {
            if (item.GistId is null)
                continue;
            gistLabelCounts[item.BaseDisplay] = gistLabelCounts.GetValueOrDefault(item.BaseDisplay) + 1;
        }

        var listed = new List<RunHistoryEntry>(pending.Count);
        foreach (var item in pending)
        {
            var display = item.BaseDisplay;
            // Colliding owner/file gists → owner/shortsha:file (ref-like, familiar).
            if (item.GistId is { } id
                && item.GistFile is { } file
                && gistLabelCounts.TryGetValue(item.BaseDisplay, out var count)
                && count > 1)
            {
                display = $"{item.GistOwner}/{ShortId(id)}:{file}";
            }

            listed.Add(new RunHistoryEntry(item.Input, item.LastUsedUtc, item.UseCount)
            {
                Display = display,
            });
        }

        listed.Sort(static (a, b) =>
        {
            var byCount = b.UseCount.CompareTo(a.UseCount);
            if (byCount != 0)
                return byCount;
            var byTime = b.LastUsedUtc.CompareTo(a.LastUsedUtc);
            if (byTime != 0)
                return byTime;
            return StringComparer.OrdinalIgnoreCase.Compare(a.Display, b.Display);
        });

        return listed;
    }

    /// <summary>
    /// Local paths must still exist; remote refs stay selectable even when not cached locally.
    /// </summary>
    internal static bool IsSelectable(string input)
    {
        try
        {
            if (File.Exists(input))
                return true;

            var full = Path.GetFullPath(input);
            if (File.Exists(full))
                return true;

            // Absolute path (drive/UNC) that is gone — drop from picker.
            if (Path.IsPathRooted(input))
                return false;
        }
        catch
        {
            // Fall through to remote-ref / opaque handling.
        }

        // owner/repo[@ref][:path] and host forms remain listed.
        if (RemoteRef.TryParse(input, out _))
            return true;

        // Relative-looking leftovers: only if they resolve to an existing file (checked above).
        return false;
    }

    /// <summary>
    /// Records (or bumps) an input in go.toml history. Local files are stored as full paths;
    /// remote refs are stored as typed.
    /// </summary>
    public static void Record(string input, string? settingsPath = null)
    {
        if (string.IsNullOrWhiteSpace(input))
            return;

        var key = NormalizeInput(input);
        var settings = SettingsStore.Load(settingsPath);
        settings.History ??= [];

        var existing = settings.History.FirstOrDefault(h => HistoryKeyEquals(h.Input, key));
        var capturedEntry = TryCaptureGistEntry(key);

        var now = DateTimeOffset.UtcNow;
        if (existing is not null)
        {
            existing.LastUsedUtc = now;
            existing.UseCount = existing.UseCount <= 0 ? 2 : existing.UseCount + 1;
            // Keep stored form stable (prefer existing casing/path form).
            // Refresh entry filename when we can resolve it (e.g. after first download).
            if (capturedEntry is not null)
                existing.Entry = capturedEntry;
        }
        else
        {
            settings.History.Add(new HistoryEntry
            {
                Input = key,
                LastUsedUtc = now,
                UseCount = 1,
                Entry = capturedEntry,
            });
        }

        SettingsStore.Save(settings, settingsPath);
    }

    /// <summary>
    /// Removes a single history entry matching <paramref name="input"/> (same identity rules as <see cref="Record"/>).
    /// Returns true if at least one entry was removed. No-op when absent.
    /// </summary>
    public static bool Remove(string input, string? settingsPath = null)
    {
        if (string.IsNullOrWhiteSpace(input))
            return false;

        var settings = SettingsStore.Load(settingsPath);
        if (settings.History is not { Count: > 0 })
            return false;

        var removed = settings.History.RemoveAll(h => HistoryKeyEquals(h.Input, input)) > 0;
        if (removed)
            SettingsStore.Save(settings, settingsPath);
        return removed;
    }

    /// <summary>
    /// Clears all MRU history entries. Preserves other go.toml fields (e.g. <c>lastCleanupUtc</c>).
    /// </summary>
    public static void Clear(string? settingsPath = null)
    {
        var settings = SettingsStore.Load(settingsPath);
        if (settings.History is not { Count: > 0 })
            return;

        settings.History = [];
        SettingsStore.Save(settings, settingsPath);
    }

    /// <summary>True when stored and candidate refer to the same history identity.</summary>
    internal static bool HistoryKeyEquals(string stored, string candidate)
    {
        if (string.IsNullOrWhiteSpace(stored) || string.IsNullOrWhiteSpace(candidate))
            return false;

        if (string.Equals(stored.Trim(), candidate.Trim(), StringComparison.OrdinalIgnoreCase))
            return true;

        var nStored = NormalizeInput(stored);
        var nCandidate = NormalizeInput(candidate);
        if (string.Equals(nStored, nCandidate, StringComparison.OrdinalIgnoreCase))
            return true;

        // Match absolute path forms even when the file no longer exists (remove after delete).
        try
        {
            var fStored = Path.GetFullPath(stored.Replace('/', Path.DirectorySeparatorChar)).Replace('\\', '/');
            var fCandidate = Path.GetFullPath(candidate.Replace('/', Path.DirectorySeparatorChar)).Replace('\\', '/');
            if (string.Equals(fStored, fCandidate, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        catch
        {
            // Not path-like.
        }

        return false;
    }

    /// <summary>
    /// Splits a user-typed argument line into argv tokens (whitespace, double-quoted groups).
    /// Empty/whitespace yields an empty array.
    /// </summary>
    public static string[] ParseArgsLine(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return [];

        var result = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;

        foreach (var c in line)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (!inQuotes && char.IsWhiteSpace(c))
            {
                if (current.Length > 0)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
                continue;
            }

            current.Append(c);
        }

        if (current.Length > 0)
            result.Add(current.ToString());

        return [.. result];
    }

    /// <summary>
    /// Maps a chosen history entry plus a typed args line to the run path inputs
    /// (entry path/ref + app argument array) used by the default command.
    /// </summary>
    public static (string Input, string[] AppArgs) ToRunInputs(string entryInput, string? argsLine)
        => (entryInput, ParseArgsLine(argsLine));

    /// <summary>
    /// Local existing files → full path with <c>/</c>; otherwise the trimmed original (remote ref).
    /// </summary>
    public static string NormalizeInput(string input)
    {
        input = input.Trim();
        try
        {
            var full = Path.GetFullPath(input);
            if (File.Exists(full))
                return full.Replace('\\', '/');
        }
        catch
        {
            // Not a filesystem path (or invalid) — treat as remote ref / opaque input.
        }

        return input;
    }

    /// <summary>
    /// Builds the base picker label. Gists become <c>owner/filename</c> when the file is known
    /// (explicit <c>:path</c>, cached history entry, or live download bundle); otherwise the
    /// normalized input. When a gist label is produced, owner/id/file outs are set for
    /// collision disambiguation as <c>owner/shortsha:file</c>.
    /// </summary>
    internal static string FormatBaseDisplay(
        string input,
        string? cachedEntry,
        out string? gistOwner,
        out string? gistId,
        out string? gistFile)
    {
        gistOwner = null;
        gistId = null;
        gistFile = null;
        if (TryGetGistParts(input, out var owner, out var id, out var remote))
        {
            var file = ResolveGistFileName(remote, cachedEntry);
            if (file is not null)
            {
                gistOwner = owner;
                gistId = id;
                gistFile = file;
                return $"{owner}/{file}";
            }
        }

        return input.Replace('\\', '/');
    }

    /// <summary>First 7 characters of a gist id (or the full id when shorter).</summary>
    internal static string ShortId(string gistId)
        => gistId.Length <= 7 ? gistId : gistId[..7];

    /// <summary>
    /// Resolves the entry file name for a gist: explicit path, history cache, remote settings,
    /// or first top-level <c>.cs</c> in the download bundle.
    /// </summary>
    internal static string? ResolveGistFileName(RemoteRef remote, string? cachedEntry)
    {
        if (!string.IsNullOrEmpty(remote.Path))
            return FileNameOnly(remote.Path);

        if (!string.IsNullOrEmpty(cachedEntry))
            return FileNameOnly(cachedEntry);

        try
        {
            if (!Directory.Exists(remote.TempPath))
                return null;

            var settings = RemoteSettingsStore.Load(remote.TempPath);
            if (!string.IsNullOrEmpty(settings.Entry))
                return FileNameOnly(settings.Entry);

            var prog = Path.Combine(remote.TempPath, "program.cs");
            if (File.Exists(prog))
                return "program.cs";

            var first = Directory.EnumerateFiles(remote.TempPath, "*.cs", SearchOption.TopDirectoryOnly).FirstOrDefault();
            if (first is not null)
                return Path.GetFileName(first);
        }
        catch
        {
            // Bundle missing or unreadable — caller falls back to raw input.
        }

        return null;
    }

    static string? TryCaptureGistEntry(string input)
    {
        if (!TryGetGistParts(input, out _, out _, out var remote))
            return null;
        return ResolveGistFileName(remote, cachedEntry: null);
    }

    static bool TryGetGistParts(
        string input,
        [NotNullWhen(true)] out string? owner,
        [NotNullWhen(true)] out string? gistId,
        [NotNullWhen(true)] out RemoteRef? remote)
    {
        owner = null;
        gistId = null;
        remote = null;

        if (!RemoteRef.TryParse(input, out var parsed))
            return false;

        if (!string.Equals(parsed.Host, "gist.github.com", StringComparison.OrdinalIgnoreCase))
            return false;

        owner = parsed.Owner;
        gistId = parsed.Repo;
        remote = parsed;
        return true;
    }

    static string FileNameOnly(string path)
        => Path.GetFileName(path.Replace('\\', '/'));
}
