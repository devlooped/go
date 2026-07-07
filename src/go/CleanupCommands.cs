using ConsoleAppFramework;

namespace Devlooped;

public class CleanupCommands
{
    [Command("cleanup")]
    [Hidden]
    public int Cleanup(int days = CleanupManager.DefaultDays) => CleanupManager.Cleanup(days);
}