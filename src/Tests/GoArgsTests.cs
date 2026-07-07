using Devlooped;

namespace Tests;

public class GoArgsTests
{
    [Fact]
    public void Split_without_separator_forwards_all_args_to_app()
    {
        var (dotnet, app) = GoArgs.Split(["--foo", "bar"]);

        Assert.Empty(dotnet);
        Assert.Equal(["--foo", "bar"], app);
    }

    [Fact]
    public void Split_with_separator_routes_dotnet_and_app_args()
    {
        var (dotnet, app) = GoArgs.Split(["-c", "Release", "--", "arg1", "arg2"]);

        Assert.Equal(["-c", "Release"], dotnet);
        Assert.Equal(["arg1", "arg2"], app);
    }

    [Fact]
    public void Split_with_trailing_separator_leaves_app_args_empty()
    {
        var (dotnet, app) = GoArgs.Split(["-c", "Release", "--"]);

        Assert.Equal(["-c", "Release"], dotnet);
        Assert.Empty(app);
    }

    [Fact]
    public void Split_with_leading_separator_leaves_dotnet_args_empty()
    {
        var (dotnet, app) = GoArgs.Split(["--", "arg1"]);

        Assert.Empty(dotnet);
        Assert.Equal(["arg1"], app);
    }

    [Fact]
    public void ApplyPublishMode_adds_r2r_properties_when_enabled()
    {
        var args = GoArgs.ApplyPublishMode(["-c", "Release"], readyToRun: true);

        Assert.Equal(["/p:PublishAot=false", "/p:PublishReadyToRun=true", "-c", "Release"], args);
    }

    [Fact]
    public void ApplyPublishMode_leaves_args_unchanged_when_disabled()
    {
        var args = GoArgs.ApplyPublishMode(["-c", "Release"], readyToRun: false);

        Assert.Equal(["-c", "Release"], args);
    }

    [Fact]
    public void Normalize_maps_go_prefixed_switches_to_bare_forms()
    {
        var normalized = GoArgs.Normalize(["--go-debug", "--go-r2r", "owner/repo", "--", "apparg"]);

        Assert.Equal(["--debug", "--r2r", "owner/repo", "--", "apparg"], normalized);
    }

    [Fact]
    public void Normalize_leaves_bare_and_unknown_untouched()
    {
        var normalized = GoArgs.Normalize(["--debug", "--r2r", "--other", "input.cs"]);

        Assert.Equal(["--debug", "--r2r", "--other", "input.cs"], normalized);
    }

    [Fact]
    public void Normalize_handles_mixed_case_and_all_forms()
    {
        var normalized = GoArgs.Normalize(["--Go-Debug", "--R2R", "ref"]);

        Assert.Equal(["--debug", "--r2r", "ref"], normalized);
    }

    [Fact]
    public void Normalize_empty_and_null()
    {
        Assert.Empty(GoArgs.Normalize([]));
        Assert.Empty(GoArgs.Normalize(null!));
    }

    [Fact]
    public void PrepareCafArgs_strips_dotnet_args_and_separator_for_default_command()
    {
        var caf = GoArgs.PrepareCafArgs(["app.cs", "/p:MyProp=true", "--", "arg1", "arg2"]);

        Assert.Equal(["app.cs"], caf);
        Assert.Equal(["/p:MyProp=true", "--", "arg1", "arg2"], GoArgs.ForwardArgs);

        var (dotnet, app) = GoArgs.Split(GoArgs.ForwardArgs);
        Assert.Equal(["/p:MyProp=true"], dotnet);
        Assert.Equal(["arg1", "arg2"], app);
    }

    [Fact]
    public void PrepareCafArgs_strips_msbuild_verbosity_before_separator()
    {
        var caf = GoArgs.PrepareCafArgs(["app.cs", "/v:q", "--", "hello", "world"]);

        Assert.Equal(["app.cs"], caf);
        Assert.Equal(["/v:q", "--", "hello", "world"], GoArgs.ForwardArgs);

        var (dotnet, app) = GoArgs.Split(GoArgs.ForwardArgs);
        Assert.Equal(["/v:q"], dotnet);
        Assert.Equal(["hello", "world"], app);
    }

    [Fact]
    public void PrepareCafArgs_keeps_go_flags_and_forwards_rest_for_dev()
    {
        var caf = GoArgs.PrepareCafArgs(["dev", "app.cs", "--r2r", "/p:Configuration=Release", "--", "apparg"]);

        Assert.Equal(["dev", "app.cs", "--r2r"], caf);
        Assert.Equal(["/p:Configuration=Release", "--", "apparg"], GoArgs.ForwardArgs);
    }

    [Fact]
    public void PrepareCafArgs_forwards_positional_args_when_no_separator()
    {
        GoArgs.PrepareCafArgs(["app.cs", "arg1", "arg2"]);

        Assert.Equal(["arg1", "arg2"], GoArgs.ForwardArgs);
    }

    [Fact]
    public void PrepareCafArgs_passes_clean_args_through_unchanged()
    {
        var caf = GoArgs.PrepareCafArgs(["clean", "--all"]);

        Assert.Equal(["clean", "--all"], caf);
        Assert.Empty(GoArgs.ForwardArgs);
    }

    [Fact]
    public void PrepareCafArgs_passes_help_through_unchanged()
    {
        var caf = GoArgs.PrepareCafArgs(["--help"]);

        Assert.Equal(["--help"], caf);
        Assert.Empty(GoArgs.ForwardArgs);
    }

    [Fact]
    public void PrepareCafArgs_maps_debug_flag_to_caf_option_name()
    {
        var caf = GoArgs.PrepareCafArgs(["app.cs", "--debug", "/p:x=1", "--", "apparg"]);

        Assert.Equal(["app.cs", "--gdbg"], caf);
        Assert.Equal(["/p:x=1", "--", "apparg"], GoArgs.ForwardArgs);
    }
}