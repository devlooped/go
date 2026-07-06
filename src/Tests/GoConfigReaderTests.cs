using System.Text;
using Devlooped;

namespace Tests;

public class BuildStateTests
{
    [Fact]
    public void TryRead_parses_multiple_inputs_and_app()
    {
        var dir = CreateTempDir();
        var inputA = WriteFile(Path.Combine(dir, "libs"), "a.cs", "a");
        var inputB = WriteFile(dir, "app.cs", "app");
        var app = WriteFile(Path.Combine(dir, "artifacts", "app"), "app.exe", "app");

        var stampPath = Path.Combine(dir, "app.stamp");
        File.WriteAllText(stampPath, $"""
            input = {ToStampPath(inputA)}
            input = {ToStampPath(inputB)}
            app = {ToStampPath(app)}
            """);

        var success = BuildState.TryRead(stampPath, out var state);

        Assert.True(success);
        Assert.NotNull(state);
        Assert.Equal(Path.GetFullPath(app), Path.GetFullPath(state.App));
        Assert.Equal(2, state.Inputs.Count);
        Assert.Contains(state.Inputs, p => Path.GetFullPath(p) == Path.GetFullPath(inputA));
        Assert.Contains(state.Inputs, p => Path.GetFullPath(p) == Path.GetFullPath(inputB));
    }

    [Fact]
    public void TryRead_parses_stamp_written_with_utf8_bom()
    {
        var dir = CreateTempDir();
        var input = WriteFile(dir, "app.cs", "app");
        var app = WriteFile(Path.Combine(dir, "artifacts"), "app.exe", "app");
        var stampPath = Path.Combine(dir, "app.stamp");

        File.WriteAllText(stampPath, string.Empty, Encoding.UTF8);
        File.AppendAllText(stampPath, $"""
            input = {ToStampPath(input)}
            app = {ToStampPath(app)}
            """);

        var success = BuildState.TryRead(stampPath, out var state);

        Assert.True(success);
        Assert.NotNull(state);
        Assert.Equal(Path.GetFullPath(app), Path.GetFullPath(state!.App));
    }

    [Fact]
    public void TryRead_parses_windows_paths_written_with_forward_slashes()
    {
        var dir = CreateTempDir();
        var input = WriteFile(dir, "app.cs", "app");
        var app = WriteFile(Path.Combine(dir, "artifacts"), "app.exe", "app");
        var stampPath = Path.Combine(dir, "app.stamp");

        File.WriteAllText(stampPath, $"""
            input = {ToStampPath(input)}
            app = {ToStampPath(app)}
            """);

        var success = BuildState.TryRead(stampPath, out var state);

        Assert.True(success);
        Assert.NotNull(state);
        Assert.Equal(Path.GetFullPath(app), Path.GetFullPath(state!.App));
    }

    [Fact]
    public void TryRead_returns_false_when_app_is_missing()
    {
        var dir = CreateTempDir();
        var stampPath = Path.Combine(dir, "app.stamp");
        File.WriteAllText(stampPath, """
            input = app.cs
            """);

        var success = BuildState.TryRead(stampPath, out var state);

        Assert.False(success);
        Assert.Null(state);
    }

    static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "go-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    static string WriteFile(string dir, string name, string content)
    {
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    static string ToStampPath(string path) => path.Replace('\\', '/');
}