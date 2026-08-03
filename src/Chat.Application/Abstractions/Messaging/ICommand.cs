using Chat.Domain.Common;
using MediatR;

namespace Chat.Application.Abstractions.Messaging;

/// <summary>A use case that mutates state and returns no payload.</summary>
public interface ICommand : IRequest<Result>;

/// <summary>A use case that mutates state and returns a minimal payload (usually an identifier).</summary>
public interface ICommand<TResponse> : IRequest<Result<TResponse>>;

public interface ICommandHandler<in TCommand> : IRequestHandler<TCommand, Result>
    where TCommand : ICommand;

public interface ICommandHandler<in TCommand, TResponse> : IRequestHandler<TCommand, Result<TResponse>>
    where TCommand : ICommand<TResponse>;
