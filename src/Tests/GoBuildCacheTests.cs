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
}