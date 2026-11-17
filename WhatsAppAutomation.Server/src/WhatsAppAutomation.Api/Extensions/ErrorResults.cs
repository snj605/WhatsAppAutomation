using Microsoft.AspNetCore.Mvc;
using WhatsAppAutomation.Application.Common.Models;

namespace WhatsAppAutomation.Api.Extensions;

internal static class ErrorResults
{
    /// <summary>Turns an application error into an RFC 9457 "problem details" response with a stable code.</summary>
    public static ObjectResult ToProblem(this ControllerBase controller, Error error)
    {
        var status = error.Kind switch
        {
            ErrorKind.Validation => StatusCodes.Status400BadRequest,
            ErrorKind.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorKind.Forbidden => StatusCodes.Status403Forbidden,
            ErrorKind.NotFound => StatusCodes.Status404NotFound,
            ErrorKind.Conflict => StatusCodes.Status409Conflict,
            ErrorKind.TooManyRequests => StatusCodes.Status429TooManyRequests,
            ErrorKind.Unavailable => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status500InternalServerError,
        };

        var problem = controller.ProblemDetailsFactory.CreateProblemDetails(
            controller.HttpContext, status, detail: error.Message);
        problem.Extensions["code"] = error.Code;

        return new ObjectResult(problem) { StatusCode = status };
    }

    public static IActionResult ToActionResult<T>(this ControllerBase controller, Result<T> result, Func<T, IActionResult> onSuccess) =>
        result.IsSuccess ? onSuccess(result.Value) : controller.ToProblem(result.Error!);

    public static IActionResult ToActionResult(this ControllerBase controller, Result result) =>
        result.IsSuccess ? controller.NoContent() : controller.ToProblem(result.Error!);
}
