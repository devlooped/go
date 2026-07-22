using Devlooped;

namespace Tests;

public class GoArgsTests
{
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
    public void ApplyVerbosity_always_appends_quiet()
    {
        Assert.Equal(["-v:quiet"], GoArgs.ApplyVerbosity([]));
        Assert.Equal(["/p:PublishAot=false", "/p:PublishReadyToRun=true", "-v:quiet"],
            GoArgs.ApplyVerbosity(GoArgs.ApplyPublishMode([], readyToRun: true)));
    }

    [Fact]
    public void Normalize_maps_go_prefixed_switches_to_bare_forms()
    {
        var normalized = GoArgs.Normalize(["--go-debug", "--go-r2r", "owner/repo", "apparg"]);

        Assert.Equal(["--debug", "--r2r", "owner/repo", "apparg"], normalized);
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
    public void PrepareCafArgs_forwards_all_trailing_tokens_as_app_args()
    {
        var caf = GoArgs.PrepareArgs(["app.cs", "/p:MyProp=true", "arg1", "arg2"]);

        Assert.Equal(["app.cs"], caf);
        Assert.Equal(["/p:MyProp=true", "arg1", "arg2"], GoArgs.ForwardArgs);
    }

    [Fact]
    public void PrepareCafArgs_forwards_verbosity_tokens_as_app_args()
    {
        var caf = GoArgs.PrepareArgs(["app.cs", "-v", "n", "hello", "world"]);

        Assert.Equal(["app.cs"], caf);
        Assert.Equal(["-v", "n", "hello", "world"], GoArgs.ForwardArgs);
    }

    [Fact]
    public void PrepareCafArgs_keeps_go_flags_and_forwards_rest_for_dev()
    {
        var caf = GoArgs.PrepareArgs(["dev", "app.cs", "--r2r", "apparg", "/p:x=1"]);

        Assert.Equal(["dev", "app.cs", "--r2r"], caf);
        Assert.Equal(["apparg", "/p:x=1"], GoArgs.ForwardArgs);
    }

    [Fact]
    public void PrepareCafArgs_forwards_positional_args_when_no_options()
    {
        GoArgs.PrepareArgs(["app.cs", "arg1", "arg2"]);

        Assert.Equal(["arg1", "arg2"], GoArgs.ForwardArgs);
    }

    [Fact]
    public void PrepareCafArgs_passes_clean_args_through_unchanged()
    {
        var caf = GoArgs.PrepareArgs(["clean", "--all"]);

        Assert.Equal(["clean", "--all"], caf);
        Assert.Empty(GoArgs.ForwardArgs);
    }

    [Fact]
    public void PrepareCafArgs_passes_remove_args_through_unchanged()
    {
        var caf = GoArgs.PrepareArgs(["remove", "app.cs", "--all"]);

        // remove owns its args (same non-forwarding path as clean); do not strip --all as app arg.
        Assert.Equal(["remove", "app.cs", "--all"], caf);
        Assert.Empty(GoArgs.ForwardArgs);
    }

    [Fact]
    public void PrepareCafArgs_passes_open_args_through_unchanged()
    {
        var caf = GoArgs.PrepareArgs(["open", "app.cs"]);

        // open owns its args (same non-forwarding path as clean/remove); do not treat path as app-forward.
        Assert.Equal(["open", "app.cs"], caf);
        Assert.Empty(GoArgs.ForwardArgs);
    }

    [Fact]
    public void PrepareCafArgs_passes_open_with_remote_ref_through_unchanged()
    {
        var caf = GoArgs.PrepareArgs(["open", "kzu/sandbox@main:program.cs"]);

        Assert.Equal(["open", "kzu/sandbox@main:program.cs"], caf);
        Assert.Empty(GoArgs.ForwardArgs);
    }

    [Fact]
    public void PrepareCafArgs_passes_open_zero_arg_through_unchanged()
    {
        var caf = GoArgs.PrepareArgs(["open"]);

        Assert.Equal(["open"], caf);
        Assert.Empty(GoArgs.ForwardArgs);
    }

    [Fact]
    public void PrepareCafArgs_passes_help_through_unchanged()
    {
        var caf = GoArgs.PrepareArgs(["--help"]);

        Assert.Equal(["--help"], caf);
        Assert.Empty(GoArgs.ForwardArgs);
    }

    [Fact]
    public void PrepareCafArgs_maps_question_mark_help_to_help()
    {
        var caf = GoArgs.PrepareArgs(["-?"]);

        Assert.Equal(["--help"], caf);
        Assert.Empty(GoArgs.ForwardArgs);
    }

    [Fact]
    public void PrepareCafArgs_maps_debug_flag_to_caf_option_name()
    {
        var caf = GoArgs.PrepareArgs(["app.cs", "--debug", "apparg"]);

        Assert.Equal(["app.cs", "--gdbg"], caf);
        Assert.Equal(["apparg"], GoArgs.ForwardArgs);
    }

    [Fact]
    public void PrepareCafArgs_does_not_split_on_double_dash()
    {
        // A bare "--" is no longer a dotnet/app separator; it is an app arg if present.
        var caf = GoArgs.PrepareArgs(["app.cs", "--", "arg1"]);

        Assert.Equal(["app.cs"], caf);
        Assert.Equal(["--", "arg1"], GoArgs.ForwardArgs);
    }

    [Fact]
    public void BuildDotnetArgs_default_is_quiet_without_r2r()
    {
        var dotnetArgs = GoArgs.ApplyVerbosity(GoArgs.ApplyPublishMode([], readyToRun: false));

        Assert.Equal(["-v:quiet"], dotnetArgs);
    }

    [Fact]
    public void BuildDotnetArgs_with_r2r_is_quiet()
    {
        var dotnetArgs = GoArgs.ApplyVerbosity(GoArgs.ApplyPublishMode([], readyToRun: true));

        Assert.Equal(["/p:PublishAot=false", "/p:PublishReadyToRun=true", "-v:quiet"], dotnetArgs);
    }
}
