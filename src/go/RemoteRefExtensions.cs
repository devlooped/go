using System.Runtime.InteropServices;

namespace Devlooped;

public static class RemoteRefExtensions
{
    extension(RemoteRef location)
    {
        public string TempPath => Path.Join(Directory.GetTempRoot(), location.Host ?? "github.com", location.Owner, location.Project ?? "", location.Repo, location.Ref ?? "main");

        public string EnsureTempPath() => Directory.CreateUserDirectory(location.TempPath);

        public string ToWebUrl()
        {
            var host = location.Host ?? "github.com";
            var reference = location.Ref ?? "main";

            return host switch
            {
                "gist.github.com" => $"https://gist.github.com/{location.Owner}/{location.Repo}",
                "gitlab.com" => location.Path != null
                    ? $"https://gitlab.com/{location.Owner}/{location.Repo}/-/blob/{reference}/{location.Path}"
                    : $"https://gitlab.com/{location.Owner}/{location.Repo}",
                "dev.azure.com" => ToAzureDevOpsWebUrl(location),
                _ => location.Path != null
                    ? $"https://github.com/{location.Owner}/{location.Repo}/blob/{reference}/{location.Path}"
                    : $"https://github.com/{location.Owner}/{location.Repo}/tree/{reference}",
            };
        }
    }

    static string ToAzureDevOpsWebUrl(RemoteRef location)
    {
        var project = location.Project ?? location.Repo;
        var url = $"https://dev.azure.com/{location.Owner}/{project}/_git/{location.Repo}";
        if (location.Path != null)
            url += $"?path=/{location.Path}";
        if (location.Ref != null)
            url += location.Path != null ? $"&version=GB{location.Ref}" : $"?version=GB{location.Ref}";
        return url;
    }
}
