namespace Devlooped;

/// <summary>A previously run go entry from root <c>go.toml</c> history.</summary>
public record RunHistoryEntry(string Input, DateTimeOffset LastUsedUtc, int UseCount)
{
    /// <summary>Display label for the picker (forward slashes for paths).</summary>
    public string Display => Input.Replace('\\', '/');
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
    /// </summary>
    public static IReadOnlyList<RunHistoryEntry> List(string? settingsPath = null)
    {
        var settings = SettingsStore.Load(settingsPath);
        if (settings.History is not { Count: > 0 })
            return [];

        return settings.History
            .Where(static h => !string.IsNullOrWhiteSpace(h.Input))
            .Where(static h => IsSelectable(h.Input))
            .Select(static h => new RunHistoryEntry(
                h.Input,
                h.LastUsedUtc == default ? DateTimeOffset.MinValue : h.LastUsedUtc,
                h.UseCount <= 0 ? 1 : h.UseCount))
            .OrderByDescending(static e => e.UseCount)
            .ThenByDescending(static e => e.LastUsedUtc)
            .ThenBy(static e => e.Display, StringComparer.OrdinalIgnoreCase)
            .ToList();
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

        var now = DateTimeOffset.UtcNow;
        if (existing is not null)
        {
            existing.LastUsedUtc = now;
            existing.UseCount = existing.UseCount <= 0 ? 2 : existing.UseCount + 1;
            // Keep stored form stable (prefer existing casing/path form).
        }
        else
        {
            settings.History.Add(new HistoryEntry
            {
                Input = key,
                LastUsedUtc = now,
                UseCount = 1,
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
}
