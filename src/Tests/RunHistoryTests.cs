using Devlooped;

namespace Tests;

public class RunHistoryTests
{
    [Fact]
    public void List_empty_history_returns_empty_without_throwing()
    {
        var path = NewSettingsPath();
        try
        {
            // Missing file
            Assert.Empty(RunHistory.List(path));

            // Existing file with no history
            SettingsStore.Save(new Settings { LastCleanupUtc = DateTimeOffset.UtcNow }, path);
            Assert.Empty(RunHistory.List(path));
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public void List_orders_most_used_first_then_recent_then_alpha()
    {
        var path = NewSettingsPath();
        try
        {
            // Use remote-ref-shaped inputs so List keeps them (local missing paths are filtered).
            var settings = new Settings
            {
                History =
                [
                    new HistoryEntry
                    {
                        Input = "z/zebra",
                        LastUsedUtc = new DateTimeOffset(2026, 1, 3, 0, 0, 0, TimeSpan.Zero),
                        UseCount = 1,
                    },
                    new HistoryEntry
                    {
                        Input = "a/alpha",
                        LastUsedUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                        UseCount = 5,
                    },
                    new HistoryEntry
                    {
                        Input = "b/beta",
                        LastUsedUtc = new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero),
                        UseCount = 5,
                    },
                    new HistoryEntry
                    {
                        Input = "g/gamma",
                        LastUsedUtc = new DateTimeOffset(2026, 1, 4, 0, 0, 0, TimeSpan.Zero),
                        UseCount = 2,
                    },
                ],
            };
            SettingsStore.Save(settings, path);

            var listed = RunHistory.List(path);

            // UseCount 5 first (beta more recent than alpha), then gamma (2), then zebra (1).
            Assert.Equal(
                ["b/beta", "a/alpha", "g/gamma", "z/zebra"],
                listed.Select(e => e.Input).ToArray());
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public void Record_bumps_use_count_and_last_used_in_go_toml()
    {
        var path = NewSettingsPath();
        try
        {
            // Remote-style inputs (no filesystem dependency)
            RunHistory.Record("owner/repo@main:app.cs", path);
            RunHistory.Record("other/proj", path);
            RunHistory.Record("owner/repo@main:app.cs", path);

            var settings = SettingsStore.Load(path);
            Assert.NotNull(settings.History);
            Assert.Equal(2, settings.History!.Count);

            var entry = settings.History.Single(h => h.Input == "owner/repo@main:app.cs");
            Assert.Equal(2, entry.UseCount);
            Assert.True(entry.LastUsedUtc > DateTimeOffset.MinValue);

            var listed = RunHistory.List(path);
            Assert.Equal("owner/repo@main:app.cs", listed[0].Input);
            Assert.Equal(2, listed[0].UseCount);
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public void Record_preserves_last_cleanup_utc()
    {
        var path = NewSettingsPath();
        try
        {
            var cleanup = new DateTimeOffset(2026, 3, 15, 12, 0, 0, TimeSpan.Zero);
            SettingsStore.Save(new Settings { LastCleanupUtc = cleanup }, path);

            RunHistory.Record("kzu/sandbox", path);

            var loaded = SettingsStore.Load(path);
            Assert.Equal(cleanup, loaded.LastCleanupUtc);
            Assert.Single(loaded.History!);
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public void Cleanup_preserves_history_when_updating_last_cleanup()
    {
        var root = Path.Combine(Path.GetTempPath(), "go-hist-clean-" + Guid.NewGuid().ToString("N"));
        var settingsPath = Path.Combine(root, "go.toml");
        Directory.CreateDirectory(root);
        try
        {
            RunHistory.Record("keep/me", settingsPath);
            CleanupManager.Cleanup(days: 0, root: root, settingsPath: settingsPath);

            var loaded = SettingsStore.Load(settingsPath);
            Assert.NotNull(loaded.LastCleanupUtc);
            Assert.Single(loaded.History!);
            Assert.Equal("keep/me", loaded.History![0].Input);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Remove_drops_matching_entry_leaves_others()
    {
        var path = NewSettingsPath();
        try
        {
            RunHistory.Record("keep/me", path);
            RunHistory.Record("drop/me", path);
            RunHistory.Record("also/keep", path);

            Assert.True(RunHistory.Remove("drop/me", path));

            var listed = RunHistory.List(path).Select(e => e.Input).OrderBy(s => s).ToArray();
            Assert.Equal(["also/keep", "keep/me"], listed);

            // Stored raw history must not contain the dropped key either.
            var settings = SettingsStore.Load(path);
            Assert.DoesNotContain(settings.History!, h => h.Input == "drop/me");
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public void Remove_absent_entry_is_noop()
    {
        var path = NewSettingsPath();
        try
        {
            RunHistory.Record("only/one", path);
            Assert.False(RunHistory.Remove("missing/ref", path));
            Assert.Single(RunHistory.List(path));
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public void Clear_empties_history_preserves_last_cleanup()
    {
        var path = NewSettingsPath();
        try
        {
            var cleanup = new DateTimeOffset(2026, 4, 1, 8, 0, 0, TimeSpan.Zero);
            SettingsStore.Save(new Settings
            {
                LastCleanupUtc = cleanup,
                History =
                [
                    new HistoryEntry { Input = "a/b", LastUsedUtc = DateTimeOffset.UtcNow, UseCount = 1 },
                    new HistoryEntry { Input = "c/d", LastUsedUtc = DateTimeOffset.UtcNow, UseCount = 2 },
                ],
            }, path);

            RunHistory.Clear(path);

            var loaded = SettingsStore.Load(path);
            Assert.Equal(cleanup, loaded.LastCleanupUtc);
            Assert.True(loaded.History is null || loaded.History.Count == 0);
            Assert.Empty(RunHistory.List(path));
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public void List_skips_missing_local_paths_keeps_remote_refs()
    {
        var path = NewSettingsPath();
        try
        {
            var missing = Path.Combine(Path.GetTempPath(), "go-hist-missing-" + Guid.NewGuid().ToString("N"), "gone.cs");
            SettingsStore.Save(new Settings
            {
                History =
                [
                    new HistoryEntry { Input = missing, LastUsedUtc = DateTimeOffset.UtcNow, UseCount = 9 },
                    new HistoryEntry { Input = "still/here", LastUsedUtc = DateTimeOffset.UtcNow, UseCount = 1 },
                ],
            }, path);

            var listed = RunHistory.List(path);
            Assert.Single(listed);
            Assert.Equal("still/here", listed[0].Input);
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public void ParseArgsLine_empty_and_multi_token_including_quotes()
    {
        Assert.Empty(RunHistory.ParseArgsLine(null));
        Assert.Empty(RunHistory.ParseArgsLine(""));
        Assert.Empty(RunHistory.ParseArgsLine("   "));

        Assert.Equal(["a", "b", "c"], RunHistory.ParseArgsLine("a b c"));
        Assert.Equal(["hello world", "x"], RunHistory.ParseArgsLine("\"hello world\" x"));
    }

    [Fact]
    public void ToRunInputs_combines_entry_and_args_line()
    {
        var (input, appArgs) = RunHistory.ToRunInputs("app.cs", "one \"two three\"");
        Assert.Equal("app.cs", input);
        Assert.Equal(["one", "two three"], appArgs);

        var empty = RunHistory.ToRunInputs("kzu/repo", "");
        Assert.Equal("kzu/repo", empty.Input);
        Assert.Empty(empty.AppArgs);
    }

    [Fact]
    public void ParseArgsLine_roundtrips_via_ToRunInputs_as_shipped_path()
    {
        // Drive the same helpers the interactive prompt uses after selection.
        var entry = new RunHistoryEntry("C:/Code/tools/app.cs", DateTimeOffset.UtcNow, 1);
        var (input, appArgs) = RunHistory.ToRunInputs(entry.Input, "arg1 arg2");
        Assert.Equal(entry.Input, input);
        Assert.Equal(["arg1", "arg2"], appArgs);
    }

    static string NewSettingsPath()
        => Path.Combine(Path.GetTempPath(), "go-hist-test-" + Guid.NewGuid().ToString("N") + ".toml");

    static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
