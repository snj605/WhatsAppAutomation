using FluentValidation;
using MediatR;
using WhatsAppAutomation.Application.Common.Models;

namespace WhatsAppAutomation.Application.Common.Behaviors;

/// <summary>
/// Runs before every handler. If the request has FluentValidation validators and any rule fails,
/// the handler is never called and a validation error comes back instead.
/// </summary>
internal sealed class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : IFailable<TResponse>
{
    public async Task<TResponse> Handle(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var context = new ValidationContext<TRequest>(request);
        var failures = new List<string>();
        foreach (var validator in validators)
        {
            var result = await validator.ValidateAsync(context, cancellationToken);
            failures.AddRange(result.Errors.Select(e => e.ErrorMessage));
        }

        return failures.Count == 0
            ? await next(cancellationToken)
            : TResponse.Fail(Error.Validation(string.Join(" ", failures)));
    }
}
