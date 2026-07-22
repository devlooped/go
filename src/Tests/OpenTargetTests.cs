using Devlooped;

namespace Tests;

public class OpenTargetTests
{
    [Fact]
    public void TryResolve_local_existing_file_returns_full_path()
    {
        var temp = Path.Combine(Path.GetTempPath(), $"go-open-{Guid.NewGuid():N}.cs");
        try
        {
            File.WriteAllText(temp, "// open target fixture");

            Assert.True(OpenTarget.TryResolve(temp, out var target, out var error));
            Assert.Null(error);
            Assert.NotNull(target);
            Assert.True(File.Exists(target));
            Assert.Equal(
                Path.GetFullPath(temp),
                Path.GetFullPath(target!),
                ignoreCase: OperatingSystem.IsWindows());
        }
        finally
        {
            TryDelete(temp);
        }
    }

    [Fact]
    public void TryResolve_local_relative_existing_file_returns_full_path()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"go-open-dir-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var name = "fixture.cs";
        var full = Path.Combine(dir, name);
        var prev = Directory.GetCurrentDirectory();
        try
        {
            File.WriteAllText(full, "// relative open fixture");
            Directory.SetCurrentDirectory(dir);

            Assert.True(OpenTarget.TryResolve(name, out var target, out var error));
            Assert.Null(error);
            Assert.Equal(Path.GetFullPath(full), Path.GetFullPath(target!), ignoreCase: OperatingSystem.IsWindows());
        }
        finally
        {
            Directory.SetCurrentDirectory(prev);
            TryDelete(full);
            try { Directory.Delete(dir, recursive: true); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public void TryResolve_remote_owner_repo_maps_to_github_web_url()
    {
        Assert.True(OpenTarget.TryResolve("kzu/sandbox", out var target, out var error));
        Assert.Null(error);
        Assert.Equal("https://github.com/kzu/sandbox/tree/main", target);
    }

    [Fact]
    public void TryResolve_remote_with_ref_and_path_maps_to_blob_url()
    {
        Assert.True(OpenTarget.TryResolve("kzu/sandbox@v1.2.3:src/hello.cs", out var target, out var error));
        Assert.Null(error);
        Assert.Equal("https://github.com/kzu/sandbox/blob/v1.2.3/src/hello.cs", target);
    }

    [Fact]
    public void TryResolve_gist_ref_maps_to_gist_url()
    {
        Assert.True(OpenTarget.TryResolve("gist.github.com/kzu/0ac826dc7de666546aaedd38e5965381", out var target, out var error));
        Assert.Null(error);
        Assert.Equal("https://gist.github.com/kzu/0ac826dc7de666546aaedd38e5965381", target);
    }

    [Fact]
    public void TryResolve_gitlab_ref_maps_to_gitlab_url()
    {
        Assert.True(OpenTarget.TryResolve("gitlab.com/kzu/runcs@main:program.cs", out var target, out var error));
        Assert.Null(error);
        Assert.Equal("https://gitlab.com/kzu/runcs/-/blob/main/program.cs", target);
    }

    [Fact]
    public void TryResolve_uses_shipped_ToWebUrl_for_remote()
    {
        // Drive the same mapping the product uses: parse + ToWebUrl must match TryResolve.
        const string input = "github.com/owner/repo@main:app.cs";
        Assert.True(RemoteRef.TryParse(input, out var remote));
        var expected = remote.ToWebUrl();

        Assert.True(OpenTarget.TryResolve(input, out var target, out _));
        Assert.Equal(expected, target);
    }

    [Fact]
    public void TryResolve_invalid_input_fails_without_target()
    {
        // Missing local file and not a remote ref (no owner/repo shape).
        var missing = Path.Combine(Path.GetTempPath(), $"go-open-missing-{Guid.NewGuid():N}.cs");
        Assert.False(File.Exists(missing));

        Assert.False(OpenTarget.TryResolve(missing, out var target, out var error));
        Assert.Null(target);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void TryResolve_empty_input_fails()
    {
        Assert.False(OpenTarget.TryResolve("", out var target, out var error));
        Assert.Null(target);
        Assert.Contains("Specify", error, StringComparison.OrdinalIgnoreCase);

        Assert.False(OpenTarget.TryResolve("   ", out target, out error));
        Assert.Null(target);
        Assert.NotNull(error);
    }

    [Fact]
    public void ShellOpen_spy_receives_resolved_local_target()
    {
        var temp = Path.Combine(Path.GetTempPath(), $"go-open-spy-{Guid.NewGuid():N}.cs");
        string? opened = null;
        try
        {
            File.WriteAllText(temp, "// spy");
            ShellOpen.OpenImpl = path =>
            {
                opened = path;
                return true;
            };

            Assert.True(OpenTarget.TryResolve(temp, out var target, out _));
            Assert.True(ShellOpen.TryOpen(target!));
            Assert.Equal(target, opened);
        }
        finally
        {
            ShellOpen.OpenImpl = null;
            TryDelete(temp);
        }
    }

    [Fact]
    public void ShellOpen_spy_receives_resolved_remote_url()
    {
        string? opened = null;
        try
        {
            ShellOpen.OpenImpl = path =>
            {
                opened = path;
                return true;
            };

            Assert.True(OpenTarget.TryResolve("kzu/sandbox@main:program.cs", out var target, out _));
            Assert.Equal("https://github.com/kzu/sandbox/blob/main/program.cs", target);
            Assert.True(ShellOpen.TryOpen(target!));
            Assert.Equal(target, opened);
        }
        finally
        {
            ShellOpen.OpenImpl = null;
        }
    }

    [Fact]
    public void ShellOpen_spy_false_means_open_failed()
    {
        try
        {
            ShellOpen.OpenImpl = _ => false;
            Assert.False(ShellOpen.TryOpen("https://example.com"));
        }
        finally
        {
            ShellOpen.OpenImpl = null;
        }
    }

    [Fact]
    public void ShellOpen_rejects_empty_without_calling_impl()
    {
        var called = false;
        try
        {
            ShellOpen.OpenImpl = _ =>
            {
                called = true;
                return true;
            };
            Assert.False(ShellOpen.TryOpen(""));
            Assert.False(ShellOpen.TryOpen("   "));
            Assert.False(called);
        }
        finally
        {
            ShellOpen.OpenImpl = null;
        }
    }

    static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* best-effort */ }
    }
}
