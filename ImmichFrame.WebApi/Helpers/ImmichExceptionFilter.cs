using ImmichFrame.Core.Api;
using ImmichFrame.Core.Exceptions;
using ImmichFrame.WebApi.Settings;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace ImmichFrame.WebApi.Helpers;

/// <summary>
/// Maps the exceptions that escape controllers to meaningful status codes instead of a bare 500.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><see cref="AssetNotFoundException"/> becomes 404: the asset is in no configured account
/// (e.g. a client still holding an id from before a settings change).</item>
/// <item>An Immich error (<see cref="ApiException"/>) or an unreachable Immich server
/// (<see cref="HttpRequestException"/>) becomes 502. Passing Immich's 401/403 through would make
/// the SPA believe its own ImmichFrame credentials were rejected.</item>
/// </list>
/// The problem detail is <see cref="ImmichErrorDescription"/>'s fixed text, never the exception
/// message, which for <see cref="ApiException"/> embeds part of the upstream response body.
/// </remarks>
public sealed class ImmichExceptionFilter(
    ProblemDetailsFactory problemDetailsFactory,
    ILogger<ImmichExceptionFilter> logger) : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        // Too late to change the status once bytes have gone out (e.g. a ranged video copy).
        if (context.HttpContext.Response.HasStarted) return;

        int status;
        string title;
        string? detail;

        switch (context.Exception)
        {
            case AssetNotFoundException:
                status = StatusCodes.Status404NotFound;
                title = "Asset not found";
                detail = null;
                logger.LogDebug("Asset not found for {Path}", context.HttpContext.Request.Path);
                break;

            case ApiException or HttpRequestException:
                status = StatusCodes.Status502BadGateway;
                title = "Immich request failed";
                detail = ImmichErrorDescription.Describe(context.Exception);
                logger.LogWarning(context.Exception, "Immich request failed for {Path}", context.HttpContext.Request.Path);
                break;

            default:
                return;
        }

        var problem = problemDetailsFactory.CreateProblemDetails(context.HttpContext, status, title, detail: detail);
        context.Result = new ObjectResult(problem) { StatusCode = status };
        context.ExceptionHandled = true;
    }
}
