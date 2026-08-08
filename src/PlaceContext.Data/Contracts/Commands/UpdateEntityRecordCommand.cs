using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;

namespace PlaceContext.Application.Features;

/// <summary>CRM-style update matching key columns. Requires <see cref="Permission.DataWrite"/>.</summary>
public sealed record UpdateEntityRecordCommand(
    Guid ProjectId,
    string TableName,
    IReadOnlyDictionary<string, string?> Keys,
    IReadOnlyDictionary<string, string?> Values) : ICommand<int>, IRequiresPermission
{
    public string RequiredPermission => Permission.DataWrite;
}
