using FluentValidation;

namespace TicketFlow.Application.Common;

public sealed class PageQueryValidator : AbstractValidator<PageQuery>
{
    public PageQueryValidator() => this.AddPagingRules();
}

internal static class ValidationExtensions
{
    public static void AddPagingRules<T>(this AbstractValidator<T> validator)
        where T : PageQuery
    {
        validator.RuleFor(q => q.Page).GreaterThanOrEqualTo(1);
        validator.RuleFor(q => q.PageSize).InclusiveBetween(1, PageQuery.MaxPageSize);
    }

    public static IRuleBuilderOptions<T, string> StrongPassword<T>(this IRuleBuilder<T, string> rule) => rule
        .NotEmpty()
        .MinimumLength(8)
        .MaximumLength(128)
        .Matches("[A-Z]").WithMessage("Password must contain an uppercase letter.")
        .Matches("[a-z]").WithMessage("Password must contain a lowercase letter.")
        .Matches("[0-9]").WithMessage("Password must contain a digit.");
}
