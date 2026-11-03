namespace PlaceContext.Application.DataSources;

public interface ISourceQueueDelivery
{
    Task PublishAsync(DataSourceDefinition source, IReadOnlyList<SourceDocument> changed,
        IReadOnlyList<string> deletedKeys, CancellationToken ct);
}
