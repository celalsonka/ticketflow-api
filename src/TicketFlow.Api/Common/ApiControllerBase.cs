using Microsoft.AspNetCore.Mvc;
using TicketFlow.Domain.Common;

namespace TicketFlow.Api.Common;

[ApiController]
public abstract class ApiControllerBase : ControllerBase
{
    /// <summary>Maps a business error to an RFC 9457 problem details response.</summary>
    protected ObjectResult Problem(Error error)
    {
        var status = error.Type switch
        {
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorType.PaymentRequired => StatusCodes.Status402PaymentRequired,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status500InternalServerError,
        };

        var problem = ProblemDetailsFactory.CreateProblemDetails(HttpContext, status, detail: error.Description);
        problem.Extensions["code"] = error.Code;

        return new ObjectResult(problem) { StatusCode = status, ContentTypes = { "application/problem+json" } };
    }

    protected ActionResult<T> OkOrProblem<T>(Result<T> result) =>
        result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
}
