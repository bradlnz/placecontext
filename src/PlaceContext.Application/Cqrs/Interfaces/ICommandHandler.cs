namespace PlaceContext.Application.Cqrs;

/// <summary>Handles a single command type. One use case per handler (SRP).</summary>
public interface ICommandHandler<in TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    Task<TResult> HandleAsync(TCommand command, CancellationToken ct = default);
}
