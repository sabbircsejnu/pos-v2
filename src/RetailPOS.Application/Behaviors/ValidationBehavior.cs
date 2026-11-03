using FluentValidation;
using FluentValidation.Results;
using MediatR;

namespace RetailPOS.Application.Behaviors;

public sealed class ValidationBehavior<TRequest, TResponse>(
    IEnumerable<IValidator<TRequest>> validators) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var failures = new List<ValidationFailure>();

        foreach (var validator in validators)
        {
            var result = await validator.ValidateAsync(request, cancellationToken);
            failures.AddRange(result.Errors);
        }

        var distinctFailures = failures
            .OrderBy(failure => failure.PropertyName, StringComparer.Ordinal)
            .ThenBy(failure => failure.ErrorMessage, StringComparer.Ordinal)
            .DistinctBy(failure => (failure.PropertyName, failure.ErrorMessage))
            .ToArray();

        if (distinctFailures.Length > 0)
        {
            throw new ValidationException(distinctFailures);
        }

        return await next();
    }
}
