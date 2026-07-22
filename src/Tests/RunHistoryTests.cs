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

            // Deleted local entries are pruned from storage (single save after list build).
            var stored = SettingsStore.Load(path);
            Assert.Single(stored.History!);
            Assert.Equal("still/here", stored.History![0].Input);
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public void List_prunes_blank_and_missing_paths_in_one_save()
    {
        var path = NewSettingsPath();
        try
        {
            var missing1 = Path.Combine(Path.GetTempPath(), "go-hist-m1-" + Guid.NewGuid().ToString("N"), "a.cs");
            var missing2 = Path.Combine(Path.GetTempPath(), "go-hist-m2-" + Guid.NewGuid().ToString("N"), "b.cs");
            SettingsStore.Save(new Settings
            {
                History =
                [
                    new HistoryEntry { Input = missing1, LastUsedUtc = DateTimeOffset.UtcNow, UseCount = 3 },
                    new HistoryEntry { Input = "   ", LastUsedUtc = DateTimeOffset.UtcNow, UseCount = 1 },
                    new HistoryEntry { Input = "keep/ref", LastUsedUtc = DateTimeOffset.UtcNow, UseCount = 2 },
                    new HistoryEntry { Input = missing2, LastUsedUtc = DateTimeOffset.UtcNow, UseCount = 4 },
                ],
            }, path);

            var listed = RunHistory.List(path);
            Assert.Single(listed);
            Assert.Equal("keep/ref", listed[0].Input);

            var stored = SettingsStore.Load(path);
            Assert.Single(stored.History!);
            Assert.Equal("keep/ref", stored.History![0].Input);
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

    [Fact]
    public void List_formats_gists_as_owner_filename_from_path_suffix()
    {
        var path = NewSettingsPath();
        try
        {
            SettingsStore.Save(new Settings
            {
                History =
                [
                    new HistoryEntry
                    {
                        Input = "gist.github.com/kzu/0ac826dc7de666546aaedd38e5965381:run.cs",
                        LastUsedUtc = DateTimeOffset.UtcNow,
                        UseCount = 1,
                    },
                    new HistoryEntry
                    {
                        Input = "kzu/sandbox",
                        LastUsedUtc = DateTimeOffset.UtcNow,
                        UseCount = 1,
                    },
                ],
            }, path);

            var listed = RunHistory.List(path);
            var gist = listed.Single(e => e.Input.Contains("0ac826dc", StringComparison.Ordinal));
            Assert.Equal("kzu/run.cs", gist.Display);

            var repo = listed.Single(e => e.Input == "kzu/sandbox");
            Assert.Equal("kzu/sandbox", repo.Display);
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public void List_formats_gists_as_owner_filename_from_cached_entry()
    {
        var path = NewSettingsPath();
        try
        {
            SettingsStore.Save(new Settings
            {
                History =
                [
                    new HistoryEntry
                    {
                        Input = "gist.github.com/kzu/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                        LastUsedUtc = DateTimeOffset.UtcNow,
                        UseCount = 1,
                        Entry = "hello.cs",
                    },
                ],
            }, path);

            var listed = RunHistory.List(path);
            Assert.Single(listed);
            Assert.Equal("kzu/hello.cs", listed[0].Display);
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public void List_disambiguates_same_owner_file_with_short_gist_id()
    {
        var path = NewSettingsPath();
        try
        {
            SettingsStore.Save(new Settings
            {
                History =
                [
                    new HistoryEntry
                    {
                        Input = "gist.github.com/kzu/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                        LastUsedUtc = DateTimeOffset.UtcNow,
                        UseCount = 2,
                        Entry = "run.cs",
                    },
                    new HistoryEntry
                    {
                        Input = "gist.github.com/kzu/bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
                        LastUsedUtc = DateTimeOffset.UtcNow,
                        UseCount = 1,
                        Entry = "run.cs",
                    },
                    new HistoryEntry
                    {
                        Input = "gist.github.com/other/cccccccccccccccccccccccccccccccc",
                        LastUsedUtc = DateTimeOffset.UtcNow,
                        UseCount = 1,
                        Entry = "run.cs",
                    },
                ],
            }, path);

            var listed = RunHistory.List(path);
            var kzuA = listed.Single(e => e.Input.Contains("aaaaaaaa", StringComparison.Ordinal));
            var kzuB = listed.Single(e => e.Input.Contains("bbbbbbbb", StringComparison.Ordinal));
            var other = listed.Single(e => e.Input.Contains("cccccccc", StringComparison.Ordinal));

            Assert.Equal("kzu/aaaaaaa:run.cs", kzuA.Display);
            Assert.Equal("kzu/bbbbbbb:run.cs", kzuB.Display);
            // Different owner — no disambiguation needed even though file name matches.
            Assert.Equal("other/run.cs", other.Display);
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public void List_gist_without_known_file_keeps_raw_input()
    {
        var path = NewSettingsPath();
        try
        {
            var input = "gist.github.com/kzu/dddddddddddddddddddddddddddddddd";
            SettingsStore.Save(new Settings
            {
                History =
                [
                    new HistoryEntry
                    {
                        Input = input,
                        LastUsedUtc = DateTimeOffset.UtcNow,
                        UseCount = 1,
                    },
                ],
            }, path);

            var listed = RunHistory.List(path);
            Assert.Single(listed);
            Assert.Equal(input, listed[0].Display);
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public void FormatBaseDisplay_and_ShortId_helpers()
    {
        Assert.Equal("aaaaaaa", RunHistory.ShortId("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"));
        Assert.Equal("short", RunHistory.ShortId("short"));

        var withPath = RunHistory.FormatBaseDisplay(
            "gist.github.com/kzu/0ac826dc7de666546aaedd38e5965381:app.cs",
            cachedEntry: null,
            out var owner, out var gistId, out var file);
        Assert.Equal("kzu/app.cs", withPath);
        Assert.Equal("kzu", owner);
        Assert.Equal("0ac826dc7de666546aaedd38e5965381", gistId);
        Assert.Equal("app.cs", file);

        var withCache = RunHistory.FormatBaseDisplay(
            "gist.github.com/kzu/0ac826dc7de666546aaedd38e5965381",
            cachedEntry: "from-cache.cs",
            out owner, out gistId, out file);
        Assert.Equal("kzu/from-cache.cs", withCache);
        Assert.Equal("kzu", owner);
        Assert.Equal("0ac826dc7de666546aaedd38e5965381", gistId);
        Assert.Equal("from-cache.cs", file);

        var repo = RunHistory.FormatBaseDisplay("kzu/sandbox", null, out owner, out gistId, out file);
        Assert.Equal("kzu/sandbox", repo);
        Assert.Null(owner);
        Assert.Null(gistId);
        Assert.Null(file);
    }

    [Fact]
    public void Record_captures_gist_entry_from_path_suffix()
    {
        var path = NewSettingsPath();
        try
        {
            RunHistory.Record("gist.github.com/kzu/0ac826dc7de666546aaedd38e5965381:run.cs", path);

            var settings = SettingsStore.Load(path);
            Assert.Single(settings.History!);
            Assert.Equal("run.cs", settings.History![0].Entry);

            var listed = RunHistory.List(path);
            Assert.Equal("kzu/run.cs", listed[0].Display);
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public void List_formats_https_gist_url_with_cached_entry()
    {
        var path = NewSettingsPath();
        try
        {
            SettingsStore.Save(new Settings
            {
                History =
                [
                    new HistoryEntry
                    {
                        Input = "https://gist.github.com/kzu/0ac826dc7de666546aaedd38e5965381",
                        LastUsedUtc = DateTimeOffset.UtcNow,
                        UseCount = 1,
                        Entry = "run.cs",
                    },
                ],
            }, path);

            var listed = RunHistory.List(path);
            Assert.Single(listed);
            Assert.Equal("kzu/run.cs", listed[0].Display);
        }
        finally
        {
            TryDelete(path);
        }
    }

    static string NewSettingsPath()
        => Path.Combine(Path.GetTempPath(), "go-hist-test-" + Guid.NewGuid().ToString("N") + ".toml");

    static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
