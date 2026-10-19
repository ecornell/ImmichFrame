using Microsoft.AspNetCore.Authentication;

public class CustomAuthenticationMiddleware
{
    private const int MaxLoggedValueLength = 120;

    private readonly RequestDelegate _next;
    private readonly ILogger<CustomAuthenticationMiddleware> _logger;

    public CustomAuthenticationMiddleware(RequestDelegate next, ILogger<CustomAuthenticationMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var result = await context.AuthenticateAsync("ImmichFrameScheme");

        if (!result.Succeeded)
        {
            var reason = result.Failure?.Message ?? "Unauthorized";

            // Names the client, so a device left with a stale or missing secret can be found.
            // The query string is left out because a page URL can carry ?authsecret=.
            _logger.LogWarning(
                "Rejected {Method} {Path} from {RemoteIp} (client '{ClientIdentifier}', user agent '{UserAgent}'): {Reason}",
                context.Request.Method,
                Sanitize(context.Request.Path.Value),
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                Sanitize(context.Request.Query["clientIdentifier"].ToString()),
                Sanitize(context.Request.Headers.UserAgent.ToString()),
                reason);

            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync(reason);
            return;
        }

        await _next(context);
    }

    /// <summary>Strips control characters, so a request cannot forge log lines, and bounds the length.</summary>
    private static string Sanitize(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var cleaned = new string(value.Where(c => !char.IsControl(c)).ToArray());
        return cleaned.Length <= MaxLoggedValueLength ? cleaned : cleaned[..MaxLoggedValueLength] + "…";
    }
}
