using System.Runtime.InteropServices;
using Devlooped;

namespace Tests;

public class NativeToolchainTests
{
    [Fact]
    public void Evaluate_missing_dotnet_fails_without_fix()
    {
        var result = NativeToolchain.Evaluate(
            dotnetPath: null,
            isOs: _ => true,
            commandExists: _ => true,
            run: (_, _) => new NativeToolchain.CapturedProcessResult(0, "C:\\VS", ""));

        Assert.False(result.Ok);
        Assert.Equal(NativeToolchain.MissingDotnetMessage, result.Message);
        Assert.Null(result.FixCommand);
    }

    [Fact]
    public void Evaluate_windows_ok_when_vs_where_returns_path()
    {
        string[]? capturedArgs = null;
        var result = NativeToolchain.Evaluate(
            dotnetPath: "dotnet",
            isOs: p => p == OSPlatform.Windows,
            commandExists: _ => false,
            run: (file, args) =>
            {
                Assert.Equal("dotnet", file);
                capturedArgs = [.. args];
                return new NativeToolchain.CapturedProcessResult(0, @"C:\Program Files\Microsoft Visual Studio\18\BuildTools", "");
            });

        Assert.True(result.Ok);
        Assert.Equal(NativeToolchain.ReadyMessage, result.Message);
        Assert.Null(result.FixCommand);
        Assert.NotNull(capturedArgs);
        Assert.Equal(
            ["dnx", "vs", "--", "where", "+vc", "--prop", "InstallationPath"],
            capturedArgs);
    }

    [Fact]
    public void Evaluate_windows_ok_when_cl_on_path_even_if_vs_fails()
    {
        var result = NativeToolchain.Evaluate(
            dotnetPath: "dotnet",
            isOs: p => p == OSPlatform.Windows,
            commandExists: name => name == "cl",
            run: (_, _) => new NativeToolchain.CapturedProcessResult(1, "", "not found"));

        Assert.True(result.Ok);
        Assert.Equal(NativeToolchain.ReadyMessage, result.Message);
    }

    [Fact]
    public void Evaluate_windows_recommends_vs_install_when_missing()
    {
        var result = NativeToolchain.Evaluate(
            dotnetPath: "dotnet",
            isOs: p => p == OSPlatform.Windows,
            commandExists: _ => false,
            run: (_, _) => new NativeToolchain.CapturedProcessResult(1, "", "not found"));

        Assert.False(result.Ok);
        Assert.Equal(NativeToolchain.MissingWindowsMessage, result.Message);
        Assert.Equal(NativeToolchain.WindowsFixCommand, result.FixCommand);
        Assert.Equal("dnx vs -- install --passive --sku:build", result.FixCommand);
    }

    [Fact]
    public void Evaluate_linux_ok_when_clang_or_gcc_present()
    {
        var withClang = NativeToolchain.Evaluate(
            dotnetPath: "dotnet",
            isOs: p => p == OSPlatform.Linux,
            commandExists: name => name == "clang",
            run: (_, _) => throw new InvalidOperationException("should not run"));

        var withGcc = NativeToolchain.Evaluate(
            dotnetPath: "dotnet",
            isOs: p => p == OSPlatform.Linux,
            commandExists: name => name == "gcc",
            run: (_, _) => throw new InvalidOperationException("should not run"));

        Assert.True(withClang.Ok);
        Assert.True(withGcc.Ok);
    }

    [Fact]
    public void Evaluate_linux_recommends_build_essential_when_missing()
    {
        var result = NativeToolchain.Evaluate(
            dotnetPath: "dotnet",
            isOs: p => p == OSPlatform.Linux,
            commandExists: _ => false,
            run: (_, _) => throw new InvalidOperationException("should not run"));

        Assert.False(result.Ok);
        Assert.Equal(NativeToolchain.MissingLinuxMessage, result.Message);
        Assert.Equal(NativeToolchain.LinuxFixCommand, result.FixCommand);
        Assert.Equal("sudo apt-get install -y build-essential", result.FixCommand);
    }

    [Fact]
    public void Evaluate_macos_ok_when_clang_present()
    {
        var result = NativeToolchain.Evaluate(
            dotnetPath: "dotnet",
            isOs: p => p == OSPlatform.OSX,
            commandExists: name => name == "clang",
            run: (_, _) => throw new InvalidOperationException("should not run"));

        Assert.True(result.Ok);
    }

    [Fact]
    public void Evaluate_macos_ok_when_xcode_select_succeeds()
    {
        var result = NativeToolchain.Evaluate(
            dotnetPath: "dotnet",
            isOs: p => p == OSPlatform.OSX,
            commandExists: _ => false,
            run: (file, args) =>
            {
                Assert.Equal("xcode-select", file);
                Assert.Equal(["-p"], args);
                return new NativeToolchain.CapturedProcessResult(0, "/Library/Developer/CommandLineTools", "");
            });

        Assert.True(result.Ok);
    }

    [Fact]
    public void Evaluate_macos_recommends_xcode_select_when_missing()
    {
        var result = NativeToolchain.Evaluate(
            dotnetPath: "dotnet",
            isOs: p => p == OSPlatform.OSX,
            commandExists: _ => false,
            run: (_, _) => new NativeToolchain.CapturedProcessResult(1, "", "error"));

        Assert.False(result.Ok);
        Assert.Equal(NativeToolchain.MissingMacMessage, result.Message);
        Assert.Equal(NativeToolchain.MacFixCommand, result.FixCommand);
        Assert.Equal("xcode-select --install", result.FixCommand);
    }

    [Fact]
    public void PrepareCafArgs_passes_check_args_through_unchanged()
    {
        var caf = GoArgs.PrepareCafArgs(["check"]);
        Assert.Equal(["check"], caf);
        Assert.Empty(GoArgs.ForwardArgs);
    }
}
