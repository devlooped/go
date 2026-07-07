namespace Devlooped;

public static class AppCleaner
{
    public static int Clean(string publishDir, string stamp)
    {
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
}