using Devlooped;

namespace Tests;

public class SkillCommandsTests
{
    [Fact]
    public void ResolveSkillPath_defaults_to_user_home()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var expected = Path.GetFullPath(Path.Combine(home, ".agents", "skills", "go-sharp", "SKILL.md"));

        Assert.Equal(expected, SkillCommands.ResolveSkillPath(null));
        Assert.Equal(expected, SkillCommands.ResolveSkillPath(""));
        Assert.Equal(expected, SkillCommands.ResolveSkillPath("   "));
    }

    [Fact]
    public void ResolveSkillPath_uses_directory_when_provided()
    {
        var root = Path.Combine(Path.GetTempPath(), "go-skill-test-" + Guid.NewGuid().ToString("N"));
        var expected = Path.GetFullPath(Path.Combine(root, ".agents", "skills", "go-sharp", "SKILL.md"));

        Assert.Equal(expected, SkillCommands.ResolveSkillPath(root));
        Assert.Equal(
            Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), ".agents", "skills", "go-sharp", "SKILL.md")),
            SkillCommands.ResolveSkillPath("."));
    }

    [Fact]
    public void Install_copies_and_overwrites_under_directory()
    {
        var bundled = Path.Combine(AppContext.BaseDirectory, "SKILL.md");
        Assert.True(File.Exists(bundled), "Bundled SKILL.md must be present in test output.");

        var root = CreateTempDir();
        var dest = SkillCommands.ResolveSkillPath(root);

        Assert.Equal(0, SkillCommands.Install(bundled, dest));
        Assert.True(File.Exists(dest));
        Assert.Equal(File.ReadAllText(bundled), File.ReadAllText(dest));

        File.WriteAllText(dest, "stale");
        Assert.Equal(0, SkillCommands.Install(bundled, dest));
        Assert.Equal(File.ReadAllText(bundled), File.ReadAllText(dest));
    }

    [Fact]
    public void Uninstall_deletes_installed_skill()
    {
        var root = CreateTempDir();
        var bundled = Path.Combine(AppContext.BaseDirectory, "SKILL.md");
        var dest = SkillCommands.ResolveSkillPath(root);

        Assert.Equal(0, SkillCommands.Install(bundled, dest));
        Assert.True(File.Exists(dest));

        Assert.Equal(0, SkillCommands.Uninstall(dest));
        Assert.False(File.Exists(dest));
        Assert.False(Directory.Exists(Path.GetDirectoryName(dest)));
    }

    [Fact]
    public void Uninstall_succeeds_when_not_installed()
    {
        var root = CreateTempDir();
        var dest = SkillCommands.ResolveSkillPath(root);

        Assert.Equal(0, SkillCommands.Uninstall(dest));
    }

    [Fact]
    public void PrepareCafArgs_passes_skill_args_through_unchanged()
    {
        var caf = GoArgs.PrepareArgs(["skill", "."]);
        Assert.Equal(["skill", "."], caf);
        Assert.Empty(GoArgs.ForwardArgs);

        caf = GoArgs.PrepareArgs(["skill", "remove", "."]);
        Assert.Equal(["skill", "remove", "."], caf);
        Assert.Empty(GoArgs.ForwardArgs);
    }

    static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "go-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
