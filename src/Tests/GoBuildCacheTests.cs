using Devlooped;

namespace Tests;

public class BuildManagerTests
{
    [Fact]
    public void IsUpToDate_returns_true_when_app_is_newer_than_all_inputs()
    {
        var dir = CreateTempDir();
        var input = WriteFile(dir, "input.cs", "input");
        var app = WriteFile(dir, "app.exe", "app");
        File.SetLastWriteTimeUtc(input, DateTime.UtcNow.AddMinutes(-5));
        File.SetLastWriteTimeUtc(app, DateTime.UtcNow);

        var state = new BuildState(app, null, [input]);

        Assert.True(BuildManager.IsUpToDate(state, app));
    }

    [Fact]
    public void IsUpToDate_returns_false_when_any_input_is_newer_than_app()
    {
        var dir = CreateTempDir();
        var input = WriteFile(dir, "input.cs", "input");
        var app = WriteFile(dir, "app.exe", "app");
        File.SetLastWriteTimeUtc(app, DateTime.UtcNow.AddMinutes(-5));
        File.SetLastWriteTimeUtc(input, DateTime.UtcNow);

        var state = new BuildState(app, null, [input]);

        Assert.False(BuildManager.IsUpToDate(state, app));
    }

    [Fact]
    public void IsUpToDate_returns_false_when_app_is_missing()
    {
        var dir = CreateTempDir();
        var input = WriteFile(dir, "input.cs", "input");
        var state = new BuildState(Path.Combine(dir, "missing.exe"), null, [input]);

        Assert.False(BuildManager.IsUpToDate(state, state.App!));
    }

    [Fact]
    public void IsUpToDate_works_with_bin_artifact()
    {
        var dir = CreateTempDir();
        var input = WriteFile(dir, "input.cs", "input");
        var bin = WriteFile(dir, "app.dll", "bin");
        File.SetLastWriteTimeUtc(input, DateTime.UtcNow.AddMinutes(-5));
        File.SetLastWriteTimeUtc(bin, DateTime.UtcNow);

        var state = new BuildState(null, bin, [input]);

        Assert.True(BuildManager.IsUpToDate(state, bin));
    }

    [Fact]
    public void IsUpToDate_returns_false_when_mode_mismatches()
    {
        var dir = CreateTempDir();
        var input = WriteFile(dir, "input.cs", "input");
        var app = WriteFile(dir, "app.exe", "app");
        File.SetLastWriteTimeUtc(input, DateTime.UtcNow.AddMinutes(-5));
        File.SetLastWriteTimeUtc(app, DateTime.UtcNow);

        var state = new BuildState(app, null, [input], PublishMode.Aot);

        Assert.False(BuildManager.IsUpToDate(state, app, PublishMode.R2r));
    }

    [Fact]
    public void IsUpToDate_returns_true_when_mode_matches()
    {
        var dir = CreateTempDir();
        var input = WriteFile(dir, "input.cs", "input");
        var app = WriteFile(dir, "app.exe", "app");
        File.SetLastWriteTimeUtc(input, DateTime.UtcNow.AddMinutes(-5));
        File.SetLastWriteTimeUtc(app, DateTime.UtcNow);

        var state = new BuildState(app, null, [input], PublishMode.R2r);

        Assert.True(BuildManager.IsUpToDate(state, app, PublishMode.R2r));
    }

    static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "go-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    static string WriteFile(string dir, string name, string content)
    {
        var path = Path.Combine(dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void TryResolveEntryPoint_prefers_FileExists_then_fallsback_to_RemoteRefTryParse()
    {
        var dir = CreateTempDir();
        var local = WriteFile(dir, "app.cs", "Console.WriteLine(1);");
        var full = Path.GetFullPath(local);

        var ok1 = Devlooped.RemoteSourceResolver.TryResolveEntryPoint(local, out var eff1, out var pdir1, out var stamp1);
        Assert.True(ok1);
        Assert.Equal(full, eff1);
        Assert.False(string.IsNullOrEmpty(pdir1));

        // non existing local + invalid ref => fail
        var bad = Path.Combine(dir, "nope.cs");
        var okBad = Devlooped.RemoteSourceResolver.TryResolveEntryPoint(bad, out _, out _, out _);
        Assert.False(okBad);

        // remote ref string (no local file) => parses, returns target path under go base
        var okRef = Devlooped.RemoteSourceResolver.TryResolveEntryPoint("kzu/runfile:run.cs", out var effRef, out var pdirRef, out _);
        Assert.True(okRef);
        Assert.Contains("run.cs", effRef);
        Assert.Contains("dotnet/go", effRef.Replace('\\', '/'));
    }

    [Fact]
    public void GetRemoteEntryPointPath_uses_path_or_program_or_persisted_marker()
    {
        // use distinct owner/repo to avoid collision with persisted markers from other tests/runs
        var uniq = Guid.NewGuid().ToString("N")[..8];
        var remoteNoPath = new Devlooped.RemoteRef("o" + uniq, "r" + uniq, null, null, null);
        var p = Devlooped.RemoteSourceResolver.GetRemoteEntryPointPath(remoteNoPath);
        Assert.EndsWith("program.cs", p);

        var withPath = new Devlooped.RemoteRef("o" + uniq, "r" + uniq, null, "src/app.cs", null);
        p = Devlooped.RemoteSourceResolver.GetRemoteEntryPointPath(withPath);
        Assert.EndsWith("src/app.cs", p.Replace('\\', '/'));

        // simulate persist via consolidated go.toml
        var dir = Devlooped.RemoteSourceResolver.GetRemoteEntryPointPath(remoteNoPath);
        var tempDir = Path.GetDirectoryName(dir)!;
        Directory.CreateDirectory(tempDir);
        var settings = new Devlooped.RemoteSettings { Entry = "actual.cs" };
        Devlooped.RemoteSettingsStore.Save(settings, tempDir);
        p = Devlooped.RemoteSourceResolver.GetRemoteEntryPointPath(remoteNoPath);
        Assert.EndsWith("actual.cs", p);

        // cleanup for hygiene
        try { File.Delete(Devlooped.RemoteSettingsStore.GetFilePath(tempDir)); } catch { }

        // NEW: exercise dir-exists + no-entry discovery branch (must prefer program.cs deterministically, write to go.toml)
        var uniq2 = Guid.NewGuid().ToString("N")[..8];
        var remoteDisc = new Devlooped.RemoteRef("disc" + uniq2, "repo" + uniq2, null, null, null);
        var cand = Devlooped.RemoteSourceResolver.GetRemoteEntryPointPath(remoteDisc);
        var discDir = Path.GetDirectoryName(cand)!;
        Directory.CreateDirectory(discDir);
        var settingsPath = Devlooped.RemoteSettingsStore.GetFilePath(discDir);
        if (File.Exists(settingsPath)) File.Delete(settingsPath);

        // write program.cs + another to test prefer
        File.WriteAllText(Path.Combine(discDir, "other.cs"), "// other");
        var prog = Path.Combine(discDir, "program.cs");
        File.WriteAllText(prog, "// prog");
        var discovered = Devlooped.RemoteSourceResolver.GetRemoteEntryPointPath(remoteDisc);
        Assert.Equal(prog, discovered);
        Assert.True(File.Exists(settingsPath));
        var s1 = Devlooped.RemoteSettingsStore.Load(discDir);
        Assert.Equal("program.cs", s1.Entry);

        // case with no program.cs: should pick the (only) remaining .cs
        File.Delete(prog);
        var other = Path.Combine(discDir, "other.cs");
        if (File.Exists(other)) File.Delete(other);
        if (File.Exists(settingsPath)) File.Delete(settingsPath);
        File.WriteAllText(Path.Combine(discDir, "only.cs"), "// only");
        var onlyDisc = Devlooped.RemoteSourceResolver.GetRemoteEntryPointPath(remoteDisc);
        Assert.EndsWith("only.cs", onlyDisc);
        var s2 = Devlooped.RemoteSettingsStore.Load(discDir);
        Assert.Equal("only.cs", s2.Entry);

        try { File.Delete(settingsPath); Directory.Delete(discDir, true); } catch { }
    }

    [Fact]
    public void IsRemoteDownloadStale_uses_2week_window_on_mtime()
    {
        var dir = CreateTempDir();
        var f = WriteFile(dir, "src.cs", "// test");
        File.SetLastWriteTimeUtc(f, DateTime.UtcNow.AddDays(-20));
        Assert.True(Devlooped.RemoteSourceResolver.IsRemoteDownloadStale(f));

        File.SetLastWriteTimeUtc(f, DateTime.UtcNow.AddDays(-3));
        Assert.False(Devlooped.RemoteSourceResolver.IsRemoteDownloadStale(f));

        var missing = Path.Combine(dir, "no.cs");
        Assert.True(Devlooped.RemoteSourceResolver.IsRemoteDownloadStale(missing));
    }

    [Fact]
    public async Task GetEffectiveSourceAsync_returns_local_existing_without_net()
    {
        var dir = CreateTempDir();
        var f = WriteFile(dir, "local.cs", "Console.WriteLine(\"hi\");");
        var eff = await Devlooped.RemoteSourceResolver.GetEffectiveSourceAsync(f, force: false);
        Assert.Equal(Path.GetFullPath(f), eff);
    }
}