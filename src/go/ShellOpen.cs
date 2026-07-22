using System.Diagnostics;

namespace Devlooped;

/// <summary>
/// Opens a local path or URL with the OS default application (shell execute).
/// </summary>
public static class ShellOpen
{
    /// <summary>
    /// Optional override for tests: when set, <see cref="TryOpen"/> delegates to this
    /// and does not start a process. Reset to null after each test that assigns it.
    /// </summary>
    internal static Func<string, bool>? OpenImpl { get; set; }

    /// <summary>
    /// Shell-opens <paramref name="pathOrUrl"/>. Returns true when the open was accepted
    /// (or the test spy succeeded). Exceptions from the OS open path yield false.
    /// </summary>
    public static bool TryOpen(string pathOrUrl)
    {
        if (string.IsNullOrWhiteSpace(pathOrUrl))
            return false;

        if (OpenImpl is not null)
            return OpenImpl(pathOrUrl);

        try
        {
            // UseShellExecute opens files/URLs via the OS association (browser, editor, etc.).
            // On Windows a null Process is normal for document/URL opens; no exception means success.
            Process.Start(new ProcessStartInfo
            {
                FileName = pathOrUrl,
                UseShellExecute = true,
            });
            return true;
        }
        catch
        {
            return false;
        }
    }
}
