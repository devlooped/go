using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Devlooped;

static class DirectoryExtensions
{
    extension(Directory)
    {
        /// <summary>Creates a temporary user-owned subdirectory for file-based apps.</summary>
        public static string CreateUserDirectory(string path)
        {
            if (OperatingSystem.IsWindows())
            {
                Directory.CreateDirectory(path);
            }
            else
            {
                // Ensure only the current user has access to the directory to avoid leaking the program to other users.
                // We don't mind that permissions might be different if the directory already exists,
                // since it's under user's local directory and its path should be unique.
                Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            return path;
        }

        public static string GetPublishDir(string entryPointFileFullPath)
        {
            // Include entry point file name so the directory name is not completely opaque.
            var fileName = Path.GetFileNameWithoutExtension(entryPointFileFullPath);
            var hash = HashWithNormalizedCasing(entryPointFileFullPath);
            var directoryName = $"{fileName}-{hash}";

            return CreateUserDirectory(Path.Combine(GetTempRoot(), directoryName));
        }

        /// <summary>Obtains the temporary directory root, e.g., <c>/tmp/dotnet/go/</c>.</summary>
        public static string GetTempRoot()
        {
            // We want a location where permissions are expected to be restricted to the current user.
            var directory = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? Path.GetTempPath()
                : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            return CreateUserDirectory(Path.Join(directory, "dotnet", "go"));
        }

        public static void Touch(string directory)
        {
            if (!Directory.Exists(directory))
                return;

            try
            {
                Directory.SetLastWriteTimeUtc(directory, DateTime.UtcNow);
            }
            catch { }
        }

        static string Hash(string text)
            => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

        static string HashWithNormalizedCasing(string text)
            => Hash(text.ToUpperInvariant());
    }
}
