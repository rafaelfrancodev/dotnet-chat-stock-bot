using Chat.Domain.Common;
using MediatR;

namespace Chat.Application.Abstractions.Messaging;

/// <summary>A read-only use case. Queries never mutate state and may bypass the domain model.</summary>
public interface IQuery<TResponse> : IRequest<Result<TResponse>>;

public interface IQueryHandler<in TQuery, TResponse> : IRequestHandler<TQuery, Result<TResponse>>
    where TQuery : IQuery<TResponse>;
