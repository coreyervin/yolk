namespace Yolk.Application.Abstractions;

// How the presentation layer calls into a slice: resolve the handler for a
// request type and call Handle. Synchronous on purpose — every slice runs on
// the owning thread (the CLI's event loop or the tray's UI thread) and none of
// them waits on anything slower than a Win32 call.

/// <summary>Handles a command that changes state and returns nothing.</summary>
public interface ICommandHandler<in TCommand>
{
    void Handle(TCommand command);
}

/// <summary>Handles a command that changes state and reports an outcome.</summary>
public interface ICommandHandler<in TCommand, out TResult>
{
    TResult Handle(TCommand command);
}

/// <summary>Handles a query, which reads state without changing it.</summary>
public interface IQueryHandler<in TQuery, out TResult>
{
    TResult Handle(TQuery query);
}
