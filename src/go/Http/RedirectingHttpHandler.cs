using System.Net;

namespace Devlooped.Http;

public sealed class RedirectingHttpHandler(HttpMessageHandler innerHandler, params string[] followHosts) : DelegatingHandler(innerHandler)
{
    static readonly string[] DefaultHosts = ["github.com", "gist.github.com", "gitlab.com", "dev.azure.com", "visualstudio.com"];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);

        var hosts = followHosts.Length > 0 ? followHosts : DefaultHosts;

        while (response.IsRedirect() && response.Headers.Location is { } location)
        {
            var redirectUri = location.IsAbsoluteUri ? location : new Uri(request.RequestUri!, location);
            // Only follow redirects for the expected hosts to avoid leaking tokens etc.
            if (!hosts.Any(h => redirectUri.Host.EndsWith(h, StringComparison.OrdinalIgnoreCase)))
                break;

            using var redirect = new HttpRequestMessage(HttpMethod.Get, redirectUri);
            // Preserve auth if present
            if (request.Headers.Authorization != null)
                redirect.Headers.Authorization = request.Headers.Authorization;
            foreach (var h in request.Headers)
                if (!redirect.Headers.Contains(h.Key))
                    redirect.Headers.TryAddWithoutValidation(h.Key, h.Value);

            response = await base.SendAsync(redirect, cancellationToken);
        }

        return response;
    }
}

static class StatusExtensions
{
    public static bool IsRedirect(this HttpResponseMessage response) =>
        response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther or
        HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;
}
