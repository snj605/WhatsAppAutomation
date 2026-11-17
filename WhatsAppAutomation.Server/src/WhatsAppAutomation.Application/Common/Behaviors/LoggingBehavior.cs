using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;
using WhatsAppAutomation.Application.Common.Models;

namespace WhatsAppAutomation.Application.Common.Behaviors;

/// <summary>
/// Logs the name, outcome and duration of every command and query. It never logs the request
/// itself, because requests carry passwords, tokens and phone numbers.
/// </summary>
internal sealed partial class LoggingBehavior<TRequest, TResponse>(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : IFailable<TResponse>
{
    public async Task<TResponse> Handle(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        var response = await next(cancellationToken);
        var elapsedMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;

        if (response.Error is { } error)
        {
            LogFailed(logger, typeof(TRequest).Name, error.Code, elapsedMs);
        }
        else
        {
            LogSucceeded(logger, typeof(TRequest).Name, elapsedMs);
        }

        return response;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "{Request} succeeded in {ElapsedMs:0} ms")]
    private static partial void LogSucceeded(ILogger logger, string request, double elapsedMs);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Request} failed with {Code} in {ElapsedMs:0} ms")]
    private static partial void LogFailed(ILogger logger, string request, string code, double elapsedMs);
}
