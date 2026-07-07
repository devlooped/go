using System.Diagnostics;
using System.Net;
using Devlooped.Http;

namespace Devlooped;

public abstract class DownloadProvider
{
    public static DownloadProvider Create(RemoteRef location) => (location.Host?.ToLowerInvariant() ?? "github.com") switch
    {
        "gitlab.com" => new GitLabDownloadProvider(),
        "dev.azure.com" => new AzureDevOpsDownloadProvider(),
        //"bitbucket.org" => new BitbucketDownloadProvider(),
        "gist.github.com" => new GitHubDownloadProvider(gist: true),
        _ => new GitHubDownloadProvider(),
    };

    public abstract Task<HttpResponseMessage> GetAsync(RemoteRef location);
}

public class GitHubDownloadProvider(bool gist = false) : DownloadProvider
{
    static readonly HttpClient http = new(new GitHubAuthHandler(
        new RedirectingHttpHandler(
            new HttpClientHandler
            {
                AllowAutoRedirect = false,
                AutomaticDecompression = DecompressionMethods.Brotli | DecompressionMethods.GZip
            })))
    {
        Timeout = Debugger.IsAttached ? Timeout.InfiniteTimeSpan : TimeSpan.FromSeconds(15)
    };

    public override async Task<HttpResponseMessage> GetAsync(RemoteRef location)
    {
        if (location.ResolvedUri != null)
        {
            return await http.SendAsync(
                new HttpRequestMessage(HttpMethod.Get, location.ResolvedUri).WithTag(location.ETag),
                HttpCompletionOption.ResponseHeadersRead);
        }

        var subdomain = gist ? "gist." : "";
        var request = new HttpRequestMessage(HttpMethod.Get,
            // Direct archive link works for branch, tag, sha
            new Uri($"https://{subdomain}github.com/{location.Owner}/{location.Repo}/archive/{location.Ref ?? "main"}.zip"))
            .WithTag(location.ETag);

        return await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
    }
}

public class GitLabDownloadProvider : DownloadProvider
{
    static readonly HttpClient http = new(new GitLabAuthHandler(
        new RedirectingHttpHandler(
            new HttpClientHandler
            {
                AllowAutoRedirect = false,
                AutomaticDecompression = DecompressionMethods.Brotli | DecompressionMethods.GZip,
            })))
    {
        Timeout = Debugger.IsAttached ? Timeout.InfiniteTimeSpan : TimeSpan.FromSeconds(15)
    };

    public override async Task<HttpResponseMessage> GetAsync(RemoteRef location)
    {
        if (location.ResolvedUri != null)
        {
            return await http.SendAsync(
                new HttpRequestMessage(HttpMethod.Get, location.ResolvedUri).WithTag(location.ETag),
                HttpCompletionOption.ResponseHeadersRead);
        }

        var url = $"https://gitlab.com/api/v4/projects/{Uri.EscapeDataString(location.Owner + "/" + location.Repo)}/repository/archive.zip?sha={location.Ref ?? "main"}";
        var request = new HttpRequestMessage(HttpMethod.Get, new Uri(url)).WithTag(location.ETag);
        var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

        return response;
    }
}

public class AzureDevOpsDownloadProvider : DownloadProvider
{
    static readonly HttpClient http = new(new AzureRepoAuthHandler(
        new RedirectingHttpHandler(
            new HttpClientHandler
            {
                AllowAutoRedirect = false,
                AutomaticDecompression = DecompressionMethods.Brotli | DecompressionMethods.GZip,
            }, "visualstudio.com")))
    {
        Timeout = Debugger.IsAttached ? Timeout.InfiniteTimeSpan : TimeSpan.FromSeconds(15)
    };

    static AzureDevOpsDownloadProvider()
    {
        http.DefaultRequestHeaders.TryAddWithoutValidation("X-TFS-FedAuthRedirect", "Suppress");
    }

    public override async Task<HttpResponseMessage> GetAsync(RemoteRef location)
    {
        if (location.ResolvedUri != null)
        {
            return await http.SendAsync(
                new HttpRequestMessage(HttpMethod.Get, location.ResolvedUri).WithTag(location.ETag),
                HttpCompletionOption.ResponseHeadersRead);
        }

        // For Azure DevOps we support dev.azure.com/org/project/repo, defaulting project=repo if not specified
        var project = location.Project ?? location.Repo;

        // Branch/ref support
        var version = location.Ref ?? "main";
        var url = $"https://dev.azure.com/{location.Owner}/{project}/_apis/git/repositories/{location.Repo}/items?download=true&version={Uri.EscapeDataString(version)}&$format=zip&api-version=7.1";
        var request = new HttpRequestMessage(HttpMethod.Get, new Uri(url)).WithTag(location.ETag);
        var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            // try tag & commit
            url = $"https://dev.azure.com/{location.Owner}/{project}/_apis/git/repositories/{location.Repo}/items?download=true&version={Uri.EscapeDataString(version)}&versionType=tag&$format=zip&api-version=7.1";
            request = new HttpRequestMessage(HttpMethod.Get, new Uri(url)).WithTag(location.ETag);
            response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                url = $"https://dev.azure.com/{location.Owner}/{project}/_apis/git/repositories/{location.Repo}/items?download=true&version={Uri.EscapeDataString(version)}&versionType=commit&$format=zip&api-version=7.1";
                request = new HttpRequestMessage(HttpMethod.Get, new Uri(url)).WithTag(location.ETag);
                response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            }
        }

        return response;
    }
}
