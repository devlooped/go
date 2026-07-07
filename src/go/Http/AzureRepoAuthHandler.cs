namespace Devlooped.Http;

/// <summary>
/// Minimal passthrough handler for public refs. (Auth paths omitted per non-goals.)
/// </summary>
public class AzureRepoAuthHandler(HttpMessageHandler inner, string? _ = null) : DelegatingHandler(inner)
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => base.SendAsync(request, cancellationToken);
}
