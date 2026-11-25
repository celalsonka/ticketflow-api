using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace TicketFlow.Api.Common;

/// <summary>
/// Runs the FluentValidation validator registered for each action argument (body, query objects)
/// and short-circuits with a 400 validation problem, so services only ever see valid input.
/// </summary>
internal sealed class ValidationFilter(IServiceProvider services, ProblemDetailsFactory problemDetailsFactory) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var errors = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var argument in context.ActionArguments.Values)
        {
            if (argument is null)
            {
                continue;
            }

            var validatorType = typeof(IValidator<>).MakeGenericType(argument.GetType());
            if (services.GetService(validatorType) is not IValidator validator)
            {
                continue;
            }

            var result = await validator.ValidateAsync(new ValidationContext<object>(argument), context.HttpContext.RequestAborted);
            foreach (var failure in result.Errors)
            {
                if (!errors.TryGetValue(failure.PropertyName, out var messages))
                {
                    errors[failure.PropertyName] = messages = [];
                }

                messages.Add(failure.ErrorMessage);
            }
        }

        if (errors.Count == 0)
        {
            await next();
            return;
        }

        var modelState = new Microsoft.AspNetCore.Mvc.ModelBinding.ModelStateDictionary();
        foreach (var (key, messages) in errors)
        {
            foreach (var message in messages)
            {
                modelState.AddModelError(key, message);
            }
        }

        var problem = problemDetailsFactory.CreateValidationProblemDetails(context.HttpContext, modelState, StatusCodes.Status400BadRequest);
        problem.Extensions["code"] = "Validation.Failed";
        context.Result = new ObjectResult(problem) { StatusCode = StatusCodes.Status400BadRequest, ContentTypes = { "application/problem+json" } };
    }
}
