using Chat.Domain.Common;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Chat.Application.Behaviors;

/// <summary>
/// Structured logging for every use case. Successes are logged at Debug on purpose: chat traffic is
/// high volume and Information-level per-message logging would flood the console (performance skill).
/// </summary>
public sealed class LoggingBehavior<TRequest, TResponse>(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : Result
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        string requestName = typeof(TRequest).Name;

        logger.LogDebug("Handling {RequestName}", requestName);

        TResponse response = await next(cancellationToken);

        if (response.IsFailure)
        {
            logger.LogWarning("{RequestName} failed with {ErrorCode}: {ErrorMessage}", requestName, response.Error.Code, response.Error.Message);
        }

        return response;
    }
}
