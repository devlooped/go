using ConsoleAppFramework;
using Spectre.Console;

namespace Devlooped;

public class SkillCommands
{
    /// <summary>Installs the bundled go-sharp agent skill (SKILL.md) for agent tooling.</summary>
    /// <param name="directory">Optional base directory. Defaults to the user home directory. Use '.' for the current directory. Writes to .agents/skills/go-sharp/SKILL.md under that base.</param>
    /// <param name="yes">-y, Skip confirmation prompt.</param>
    [Command("skill")]
    public int Skill([Argument] string? directory = null, bool yes = false)
    {
        var dest = ResolveSkillPath(directory);
        var source = Path.Combine(AppContext.BaseDirectory, "SKILL.md");
        if (!File.Exists(source))
        {
            ConsoleApp.LogError($"Bundled skill not found: {source}");
            return 1;
        }

        if (!yes && !AnsiConsole.Confirm($"Install go-sharp skill to [green]{Markup.Escape(dest)}[/]?", defaultValue: true))
            return 0;

        return Install(source, dest);
    }

    /// <summary>Removes a previously installed go-sharp agent skill.</summary>
    /// <param name="directory">Optional base directory. Defaults to the user home directory. Use '.' for the current directory.</param>
    /// <param name="yes">-y, Skip confirmation prompt.</param>
    [Command("skill remove")]
    public int Remove([Argument] string? directory = null, bool yes = false)
    {
        var dest = ResolveSkillPath(directory);

        if (!yes && !AnsiConsole.Confirm($"Remove go-sharp skill from [yellow]{Markup.Escape(dest)}[/]?", defaultValue: true))
            return 0;

        return Uninstall(dest);
    }

    internal static int Install(string source, string dest)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(source, dest, overwrite: true);
            return 0;
        }
        catch (Exception ex)
        {
            ConsoleApp.LogError($"Failed to install skill: {ex.Message}");
            return 1;
        }
    }

    internal static int Uninstall(string dest)
    {
        try
        {
            if (File.Exists(dest))
                File.Delete(dest);

            // Remove empty skill directory left behind after uninstall.
            var skillDir = Path.GetDirectoryName(dest);
            if (skillDir is not null &&
                Directory.Exists(skillDir) &&
                !Directory.EnumerateFileSystemEntries(skillDir).Any())
            {
                Directory.Delete(skillDir);
            }

            return 0;
        }
        catch (Exception ex)
        {
            ConsoleApp.LogError($"Failed to remove skill: {ex.Message}");
            return 1;
        }
    }

    /// <summary>
    /// Resolves the install path for the go-sharp skill under the given base directory
    /// (or the user home directory when <paramref name="directory"/> is omitted).
    /// </summary>
    internal static string ResolveSkillPath(string? directory)
    {
        var root = string.IsNullOrWhiteSpace(directory)
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            : Path.GetFullPath(directory);

        return Path.GetFullPath(Path.Combine(root, ".agents", "skills", "go-sharp", "SKILL.md"));
    }
}
