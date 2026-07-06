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
}