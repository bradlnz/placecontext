namespace PlaceContext.Application.Build;

public interface IModelCredentialsResolver
{
    Task<ModelCredentials> ResolveAsync(Guid? projectId, CancellationToken ct = default);
}
