using System.Text.Json;

namespace PlaceContext.Application.Features;

public readonly record struct FlattenedJsonLeaf(string Path, JsonElement Value);
