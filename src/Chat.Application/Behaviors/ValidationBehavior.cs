using Chat.Domain.Common;
using FluentValidation;
using MediatR;

namespace Chat.Application.Behaviors;

/// <summary>
/// Runs every registered FluentValidation validator before the handler. Validation failures are
/// expected failures, so they are returned as a failed <see cref="Result"/> instead of thrown.
/// </summary>
public sealed class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : Result
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        IValidator<TRequest>[] applicable = [.. validators];
        if (applicable.Length == 0)
        {
            return await next(cancellationToken);
        }

        ValidationContext<TRequest> context = new(request);

        string[] failures = [.. (await Task.WhenAll(applicable.Select(v => v.ValidateAsync(context, cancellationToken))))
            .SelectMany(result => result.Errors)
            .Where(failure => failure is not null)
            .Select(failure => failure.ErrorMessage)
            .Distinct()];

        if (failures.Length == 0)
        {
            return await next(cancellationToken);
        }

        return CreateValidationFailure(Error.Validation(ValidationErrorCode, string.Join(" ", failures)));
    }

    private const string ValidationErrorCode = "Validation.Failed";

    private static TResponse CreateValidationFailure(Error error)
    {
        if (typeof(TResponse) == typeof(Result))
        {
            return (TResponse)(object)Result.Failure(error);
        }

        object failure = typeof(Result)
            .GetMethod(nameof(Result.Failure), genericParameterCount: 1, [typeof(Error)])!
            .MakeGenericMethod(typeof(TResponse).GenericTypeArguments[0])
            .Invoke(null, [error])!;

        return (TResponse)failure;
    }
}
