using MediatR;
using WhatsAppAutomation.Application.Common.Models;

namespace WhatsAppAutomation.Application.Common.Messaging;

// CQRS: every use case is either a command (changes data) or a query (only reads).
// Both are MediatR requests that return a Result, so callers handle failures the same way.

/// <summary>Changes data and returns a value, e.g. the id of what was created.</summary>
public interface ICommand<TResponse> : IRequest<Result<TResponse>>;

/// <summary>Changes data and returns only success or failure.</summary>
public interface ICommand : IRequest<Result>;

/// <summary>Reads data. Must not change anything.</summary>
public interface IQuery<TResponse> : IRequest<Result<TResponse>>;

public interface ICommandHandler<in TCommand, TResponse> : IRequestHandler<TCommand, Result<TResponse>>
    where TCommand : ICommand<TResponse>;

public interface ICommandHandler<in TCommand> : IRequestHandler<TCommand, Result>
    where TCommand : ICommand;

public interface IQueryHandler<in TQuery, TResponse> : IRequestHandler<TQuery, Result<TResponse>>
    where TQuery : IQuery<TResponse>;
