using Devlooped;

namespace Tests;

public class GoArgsTests
{
    [Fact]
    public void Split_without_separator_forwards_all_args_to_app()
    {
        var (publish, app) = GoArgs.Split(["--foo", "bar"]);

        Assert.Empty(publish);
        Assert.Equal(["--foo", "bar"], app);
    }

    [Fact]
    public void Split_with_separator_routes_publish_and_app_args()
    {
        var (publish, app) = GoArgs.Split(["-c", "Release", "--", "arg1", "arg2"]);

        Assert.Equal(["-c", "Release"], publish);
        Assert.Equal(["arg1", "arg2"], app);
    }

    [Fact]
    public void Split_with_trailing_separator_leaves_app_args_empty()
    {
        var (publish, app) = GoArgs.Split(["-c", "Release", "--"]);

        Assert.Equal(["-c", "Release"], publish);
        Assert.Empty(app);
    }

    [Fact]
    public void Split_with_leading_separator_leaves_publish_args_empty()
    {
        var (publish, app) = GoArgs.Split(["--", "arg1"]);

        Assert.Empty(publish);
        Assert.Equal(["arg1"], app);
    }
}