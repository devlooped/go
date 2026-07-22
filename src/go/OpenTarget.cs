using System.Diagnostics.CodeAnalysis;

namespace Devlooped;

/// <summary>
/// Pure resolution of an open input to a local file path or remote web URL.
/// </summary>
public static class OpenTarget
{
    /// <summary>
    /// Maps <paramref name="input"/> to a shell-open target string.
    /// Existing local files resolve to their full path; remote refs resolve via <see cref="RemoteRefExtensions.ToWebUrl"/>.
    /// </summary>
    public static bool TryResolve(string input, [NotNullWhen(true)] out string? target, [NotNullWhen(false)] out string? error)
    {
        target = null;
        error = null;

        if (string.IsNullOrWhiteSpace(input))
        {
            error = "Specify a .cs file or remote ref to open.";
            return false;
        }

        input = input.Trim();

        // Prefer local existence (same order as clean/run artifact paths).
        try
        {
            var full = Path.GetFullPath(input);
            if (File.Exists(full))
            {
                target = full;
                return true;
            }
        }
        catch
        {
            // Not a usable filesystem path — fall through to remote-ref parsing.
        }

        if (RemoteRef.TryParse(input, out var remote))
        {
            target = remote.ToWebUrl();
            return true;
        }

        error = $"Not a local file or remote ref: {input}";
        return false;
    }
}
