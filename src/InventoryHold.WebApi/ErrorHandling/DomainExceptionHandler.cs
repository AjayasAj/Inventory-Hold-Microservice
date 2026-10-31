using InventoryHold.Domain.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace InventoryHold.WebApi.ErrorHandling;

/// <summary>
/// Maps domain exceptions to RFC 7807 problem responses. Only <see cref="DomainException"/>s are
/// handled here; anything else falls through to the default handler as a generic 500 that does not
/// leak internals (so a driver's InvalidOperationException can never masquerade as a 409).
/// </summary>
public sealed class DomainExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public static int StatusCodeFor(DomainException exception) => exception switch
    {
        HoldValidationException => StatusCodes.Status400BadRequest,
        HoldNotFoundException => StatusCodes.Status404NotFound,
        InsufficientStockException => StatusCodes.Status409Conflict,
        HoldNotActiveException => StatusCodes.Status409Conflict,
        HoldExpiredException => StatusCodes.Status410Gone,
        ProductNotFoundException => StatusCodes.Status422UnprocessableEntity,
        _ => StatusCodes.Status400BadRequest
    };

    public static ProblemDetails ToProblem(DomainException exception)
    {
        var status = StatusCodeFor(exception);
        var problem = new ProblemDetails
        {
            Status = status,
            Title = TitleFor(exception),
            Detail = exception.Message,
            Type = $"https://httpstatuses.io/{status}"
        };
        problem.Extensions["code"] = exception.Code;

        switch (exception)
        {
            case InsufficientStockException stock:
                problem.Extensions["productId"] = stock.ProductId;
                problem.Extensions["requested"] = stock.Requested;
                problem.Extensions["available"] = stock.Available;
                break;
            case ProductNotFoundException product:
                problem.Extensions["productId"] = product.ProductId;
                break;
            case HoldNotActiveException notActive:
                problem.Extensions["holdId"] = notActive.HoldId;
                problem.Extensions["status"] = notActive.Status.ToString();
                break;
            case HoldNotFoundException notFound:
                problem.Extensions["holdId"] = notFound.HoldId;
                break;
            case HoldExpiredException expired:
                problem.Extensions["holdId"] = expired.HoldId;
                break;
        }

        return problem;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken ct)
    {
        if (exception is not DomainException domainException)
        {
            return false;
        }

        var problem = ToProblem(domainException);
        httpContext.Response.StatusCode = problem.Status!.Value;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception
        });
    }

    private static string TitleFor(DomainException exception) => exception switch
    {
        HoldValidationException => "Invalid request",
        HoldNotFoundException => "Hold not found",
        InsufficientStockException => "Insufficient stock",
        HoldNotActiveException => "Hold is not active",
        HoldExpiredException => "Hold expired",
        ProductNotFoundException => "Unknown product",
        _ => "Request failed"
    };
}
