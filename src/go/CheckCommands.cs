using ConsoleAppFramework;

namespace Devlooped;

public class CheckCommands
{
    /// <summary>Verifies the native toolchain required for native AOT publishes.</summary>
    [Command("check")]
    public int Check()
    {
        var result = NativeToolchain.Evaluate();
        if (result.Ok)
        {
            Console.WriteLine(result.Message);
            return 0;
        }

        ConsoleApp.LogError(result.Message);
        if (result.FixCommand is not null)
        {
            Console.WriteLine("To fix, run:");
            Console.WriteLine($"  {result.FixCommand}");
        }

        return 1;
    }
}
