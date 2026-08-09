using System.Text.Json;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Projects.Infrastructure.Persistence;

internal static class JsonCodec
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static string Encode(IReadOnlyList<string> items)
        => JsonSerializer.Serialize(items, Options);

    public static IReadOnlyList<string> DecodeStrings(string? json)
        => string.IsNullOrWhiteSpace(json)
            ? Array.Empty<string>()
            : JsonSerializer.Deserialize<List<string>>(json, Options) ?? new List<string>();

    public static string? EncodeSnapshot(GraphSnapshotRef? snapshot)
    {
        if (snapshot is null) return null;
        var dto = new SnapshotDto(
            snapshot.GraphJsonPath,
            snapshot.BuiltAt,
            snapshot.NodeCount,
            snapshot.LinkCount,
            snapshot.GodNodes
                .Select(node => new SnapshotGodNodeDto(node.Id.Value, node.Label.Value, node.Degree))
                .ToList());
        return JsonSerializer.Serialize(dto, Options);
    }

    public static GraphSnapshotRef? DecodeSnapshot(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        var dto = JsonSerializer.Deserialize<SnapshotDto>(json, Options);
        if (dto is null) return null;
        var godNodes = dto.Gods.Select(node =>
            GodNode.Of(GraphNodeId.From(node.Id), NormLabel.From(node.Label), node.Degree));
        return GraphSnapshotRef.Of(dto.Path, dto.BuiltAt, dto.NodeCount, dto.LinkCount, godNodes);
    }
}
