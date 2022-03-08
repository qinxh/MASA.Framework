namespace Masa.Contrib.ReadWriteSpliting.Cqrs.Queries;

public abstract class QueryHandler<TQuery, TResult> : IQueryHandler<TQuery, TResult>
    where TQuery : IQuery<TResult>
    where TResult : notnull
{
    public abstract Task HandleAsync(TQuery @event);
}