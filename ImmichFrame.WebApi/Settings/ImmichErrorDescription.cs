using System.Net.Http;
using ImmichFrame.Core.Api;

namespace ImmichFrame.WebApi.Settings;

/// <summary>
/// Turns a failed call to an Immich server into a short message that is safe to show in the
/// settings page.
/// </summary>
/// <remarks>
/// Raw exception text is not safe: NSwag's <see cref="ApiException"/> message embeds up to 512
/// characters of the response body. The server URL is caller-chosen, so echoing it would let the
/// settings API read pages from any host the server can reach. Callers log the full exception.
/// </remarks>
public static class ImmichErrorDescription
{
    public static string Describe(Exception ex) => ex switch
    {
        ApiException { StatusCode: 401 or 403 } => "The Immich server rejected the API key.",
        ApiException { StatusCode: 404 } => "No Immich API found at this URL (HTTP 404).",
        ApiException api => $"The Immich server returned HTTP {api.StatusCode}.",
        HttpRequestException { HttpRequestError: HttpRequestError.NameResolutionError } =>
            "Could not resolve the server's host name.",
        HttpRequestException { HttpRequestError: HttpRequestError.SecureConnectionError } =>
            "Could not establish a secure (TLS) connection to the server.",
        HttpRequestException => "Could not connect to the Immich server.",
        OperationCanceledException => "The Immich server did not respond in time.",
        UriFormatException or InvalidOperationException => "The Immich server URL is not valid.",
        _ => "The request to the Immich server failed."
    };
}
