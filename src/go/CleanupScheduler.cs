using System.Diagnostics;
using System.Reflection;

namespace Devlooped;

public static class CleanupScheduler
{
    public static bool ShouldRun(int days = CleanupManager.DefaultDays, string? settingsPath = null)
    {
        var settings = SettingsStore.Load(settingsPath);
        if (settings.LastCleanupUtc is null)
            return true;

        return settings.LastCleanupUtc < DateTimeOffset.UtcNow.AddDays(-days);
    }

    public static void StartDetached()
    {
        if (CreateCleanupProcessStartInfo() is { } startInfo)
            Process.Start(startInfo);
    }

    public static Task TryScheduleAsync()
    {
        return Task.Run(() =>
        {
            try
            {
                if (!ShouldRun())
                    return;

                StartDetached();
            }
            catch
            {
            }
        });
    }

    static ProcessStartInfo? CreateCleanupProcessStartInfo()
    {
        var commandLine = Environment.GetCommandLineArgs();
        var fileName = Environment.ProcessPath ?? commandLine[0];

        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        if (IsDotnetHost(fileName) &&
            commandLine.Length >= 3 &&
            commandLine[1] == "exec" &&
            commandLine[2].EndsWith("go.dll", StringComparison.OrdinalIgnoreCase))
        {
            startInfo.ArgumentList.Add("exec");
            startInfo.ArgumentList.Add(commandLine[2]);
        }
        else if (IsDotnetHost(fileName))
        {
            var assembly = Assembly.GetEntryAssembly()?.Location;
            if (string.IsNullOrEmpty(assembly))
                return null;

            startInfo.ArgumentList.Add("exec");
            startInfo.ArgumentList.Add(assembly);
        }

        startInfo.ArgumentList.Add("cleanup");
        return startInfo;
    }

    static bool IsDotnetHost(string path)
        => Path.GetFileNameWithoutExtension(path).Equals("dotnet", StringComparison.OrdinalIgnoreCase);
}