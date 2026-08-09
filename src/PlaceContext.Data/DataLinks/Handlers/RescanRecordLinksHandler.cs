using PlaceContext.Application.Cqrs;

namespace PlaceContext.Application.Features;

public sealed class RescanRecordLinksHandler
    : ICommandHandler<RescanRecordLinksCommand, RecordLinkRescanResult>
{
    private readonly RecordLinkService _links;

    public RescanRecordLinksHandler(RecordLinkService links) => _links = links;

    public Task<RecordLinkRescanResult> HandleAsync(
        RescanRecordLinksCommand command,
        CancellationToken ct = default)
        => _links.RescanProjectAsync(command.ProjectId, ct);
}
