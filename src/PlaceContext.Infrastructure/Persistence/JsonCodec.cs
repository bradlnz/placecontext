using System.Text.Json;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Infrastructure.Persistence;

/// <summary>Serializes the few composite value objects we persist as JSON columns.</summary>
internal static class JsonCodec
{
    private static readonly JsonSerializerOptions Opts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static string Encode(IReadOnlyList<string> items) => JsonSerializer.Serialize(items, Opts);

    public static IReadOnlyList<string> DecodeStrings(string? json)
        => string.IsNullOrWhiteSpace(json) ? Array.Empty<string>()
            : JsonSerializer.Deserialize<List<string>>(json, Opts) ?? new();

    public static string? EncodeSnapshot(GraphSnapshotRef? s)
    {
        if (s is null) return null;
        var dto = new DecisionSnapshotDto(s.GraphJsonPath, s.BuiltAt, s.NodeCount, s.LinkCount,
            s.GodNodes.Select(g => new DecisionSnapshotGodNodeDto(g.Id.Value, g.Label.Value, g.Degree)).ToList());
        return JsonSerializer.Serialize(dto, Opts);
    }

    public static GraphSnapshotRef? DecodeSnapshot(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        var dto = JsonSerializer.Deserialize<DecisionSnapshotDto>(json, Opts);
        if (dto is null) return null;
        var gods = dto.Gods.Select(g => GodNode.Of(GraphNodeId.From(g.Id), NormLabel.From(g.Label), g.Degree));
        return GraphSnapshotRef.Of(dto.Path, dto.BuiltAt, dto.NodeCount, dto.LinkCount, gods);
    }
}
