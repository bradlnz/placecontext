namespace PlaceContext.Application.Cqrs;

/// <summary>Marker for a command (a state-changing intent) that returns <typeparamref name="TResult"/>.</summary>
public interface ICommand<TResult> { }
