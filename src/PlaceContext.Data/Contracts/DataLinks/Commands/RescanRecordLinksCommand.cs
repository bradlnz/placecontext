using PlaceContext.Application.Cqrs;

namespace PlaceContext.Application.Features;

public sealed record RescanRecordLinksCommand(Guid ProjectId)
    : ICommand<RecordLinkRescanResult>;
