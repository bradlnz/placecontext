using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;

namespace PlaceContext.Application.Features;

/// <summary>CRM-style delete matching key columns. Requires <see cref="Permission.DataWrite"/>.</summary>
public sealed record DeleteEntityRecordCommand(
    Guid ProjectId,
    string TableName,
    IReadOnlyDictionary<string, string?> Keys) : ICommand<int>, IRequiresPermission
{
    public string RequiredPermission => Permission.DataWrite;
}
