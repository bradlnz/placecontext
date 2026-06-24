namespace PlaceContext.Application.Cqrs;

/// <summary>Marker for a query (a read) that returns <typeparamref name="TResult"/>.</summary>
public interface IQuery<TResult> { }
