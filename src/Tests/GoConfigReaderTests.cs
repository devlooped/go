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
        Assert.Equal(Path.GetFullPath(app), Path.GetFullPath(state.App!));
        Assert.Null(state.Bin);
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
        Assert.Equal(Path.GetFullPath(app), Path.GetFullPath(state!.App!));
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
        Assert.Equal(Path.GetFullPath(app), Path.GetFullPath(state!.App!));
    }

    [Fact]
    public void TryRead_parses_bin_and_uses_last_bin_when_duplicated()
    {
        var dir = CreateTempDir();
        var input = WriteFile(dir, "app.cs", "app");
        var binOld = WriteFile(dir, "old.dll", "old");
        var binNew = WriteFile(dir, "new.dll", "new");
        var stampPath = Path.Combine(dir, "app.stamp");

        File.WriteAllText(stampPath, $"""
            input = {ToStampPath(input)}
            bin = {ToStampPath(binOld)}
            bin = {ToStampPath(binNew)}
            """);

        var success = BuildState.TryRead(stampPath, out var state);

        Assert.True(success);
        Assert.NotNull(state);
        Assert.Equal(Path.GetFullPath(binNew), Path.GetFullPath(state!.Bin!));
    }

    [Fact]
    public void TryRead_succeeds_with_bin_only_no_app()
    {
        var dir = CreateTempDir();
        var input = WriteFile(dir, "app.cs", "app");
        var bin = WriteFile(dir, "app.dll", "bin");
        var stampPath = Path.Combine(dir, "app.stamp");

        File.WriteAllText(stampPath, $"""
            input = {ToStampPath(input)}
            bin = {ToStampPath(bin)}
            """);

        var success = BuildState.TryRead(stampPath, out var state);

        Assert.True(success);
        Assert.NotNull(state);
        Assert.Null(state.App);
        Assert.Equal(Path.GetFullPath(bin), Path.GetFullPath(state.Bin!));
    }

    [Fact]
    public void TryRead_returns_false_when_no_inputs()
    {
        var dir = CreateTempDir();
        var stampPath = Path.Combine(dir, "app.stamp");
        File.WriteAllText(stampPath, """
            app = app.exe
            """);

        var success = BuildState.TryRead(stampPath, out var state);

        Assert.False(success);
        Assert.Null(state);
    }

    [Fact]
    public void TryRead_defaults_mode_to_aot_when_missing()
    {
        var dir = CreateTempDir();
        var input = WriteFile(dir, "app.cs", "app");
        var stampPath = Path.Combine(dir, "app.stamp");

        File.WriteAllText(stampPath, $"""
            input = {ToStampPath(input)}
            """);

        var success = BuildState.TryRead(stampPath, out var state);

        Assert.True(success);
        Assert.Equal(PublishMode.Aot, state!.Mode);
    }

    [Fact]
    public void InitialContent_writes_mode_up_front()
    {
        Assert.Equal("mode=aot" + Environment.NewLine, BuildState.InitialContent(PublishMode.Aot));
        Assert.Equal("mode=r2r" + Environment.NewLine, BuildState.InitialContent(PublishMode.R2r));
    }

    [Fact]
    public void TryRead_parses_mode_r2r()
    {
        var dir = CreateTempDir();
        var input = WriteFile(dir, "app.cs", "app");
        var stampPath = Path.Combine(dir, "app.stamp");

        File.WriteAllText(stampPath, $"""
            mode = r2r
            input = {ToStampPath(input)}
            """);

        var success = BuildState.TryRead(stampPath, out var state);

        Assert.True(success);
        Assert.Equal(PublishMode.R2r, state!.Mode);
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