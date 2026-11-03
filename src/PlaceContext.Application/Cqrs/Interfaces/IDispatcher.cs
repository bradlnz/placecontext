namespace PlaceContext.Application.Cqrs;

/// <summary>
/// Hand-rolled mediator: resolves the right handler from the container and invokes it. Keeps the
/// Presentation layer free of handler wiring without taking a MediatR dependency.
/// </summary>
public interface IDispatcher
{
    Task<TResult> Send<TResult>(ICommand<TResult> command, CancellationToken ct = default);
    Task<TResult> Query<TResult>(IQuery<TResult> query, CancellationToken ct = default);
}
