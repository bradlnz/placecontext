using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;

namespace PlaceContext.Application.Features;

/// <summary>CRM-style insert of one entity/table row. Requires <see cref="Permission.DataWrite"/>.</summary>
public sealed record CreateEntityRecordCommand(
    Guid ProjectId,
    string TableName,
    IReadOnlyDictionary<string, string?> Values) : ICommand<bool>, IRequiresPermission
{
    public string RequiredPermission => Permission.DataWrite;
}

/// <summary>CRM-style update matching key columns. Requires <see cref="Permission.DataWrite"/>.</summary>
public sealed record UpdateEntityRecordCommand(
    Guid ProjectId,
    string TableName,
    IReadOnlyDictionary<string, string?> Keys,
    IReadOnlyDictionary<string, string?> Values) : ICommand<int>, IRequiresPermission
{
    public string RequiredPermission => Permission.DataWrite;
}

/// <summary>CRM-style delete matching key columns. Requires <see cref="Permission.DataWrite"/>.</summary>
public sealed record DeleteEntityRecordCommand(
    Guid ProjectId,
    string TableName,
    IReadOnlyDictionary<string, string?> Keys) : ICommand<int>, IRequiresPermission
{
    public string RequiredPermission => Permission.DataWrite;
}
