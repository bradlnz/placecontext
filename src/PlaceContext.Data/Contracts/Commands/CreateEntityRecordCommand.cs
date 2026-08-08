using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;

namespace PlaceContext.Application.Features;

/// <summary>CRM-style insert of one entity/table row. Requires <see cref="Permission.DataWrite"/>.</summary>
public sealed record CreateEntityRecordCommand(
    Guid ProjectId,
    string TableName,
    IReadOnlyDictionary<string, string?> Values) : ICommand<CreateEntityRecordResult>, IRequiresPermission
{
    public string RequiredPermission => Permission.DataWrite;
}
