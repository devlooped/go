namespace Devlooped;

public static class AppCleaner
{
    public static int Clean(string publishDir, string stamp, string? cs = null)
    {
        if (cs is not null)
            TryDotnetClean(cs);

        try
        {
            if (Directory.Exists(publishDir))
                Directory.Delete(publishDir, recursive: true);

            return 0;
        }
        catch
        {
            try
            {
                if (File.Exists(stamp))
                    File.Delete(stamp);

                return 0;
            }
            catch
            {
                return 1;
            }
        }
    }

    static void TryDotnetClean(string cs)
    {
        var dotnet = DotnetMuxer.Path?.FullName;
        if (dotnet is null || !File.Exists(cs))
            return;

        try
        {
            ProcessRunner.DotnetCleanAsync(dotnet, cs).GetAwaiter().GetResult();
        }
        catch
        {
            // Best-effort: publish-dir cleanup still proceeds.
        }
    }
}