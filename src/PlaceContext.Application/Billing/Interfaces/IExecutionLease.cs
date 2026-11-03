namespace PlaceContext.Application.Billing;

public interface IExecutionLease : IAsyncDisposable
{
    /// <summary>Cancelled when the lease cannot be kept alive; execution must stop.</summary>
    CancellationToken LostToken
    {
        get;
    }
}
