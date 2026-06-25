using PlaceContext.Application.Dtos;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

/// <summary>
/// Shared Application-layer helper: resolves a <see cref="WorkloadSource"/> from the nullable
/// image / runtimeId / source / file-set fields carried on Create/Update commands.
/// Exactly one variant must be specified (image XOR code); throws ArgumentException otherwise.
/// When both a single <c>source</c> string and a <c>files</c> set are present, the file set wins.
/// </summary>
internal static class WorkloadSourceResolver
{
    /// <summary>
    /// Resolves to ImageWorkload when <paramref name="image"/> is provided, or CodeWorkload when
    /// <paramref name="runtimeId"/> + (<paramref name="files"/> or <paramref name="source"/>) are provided.
    /// Returns null when no variant is specified (caller decides whether null is valid).
    /// </summary>
    public static WorkloadSource? Resolve(
        string? image,
        string? runtimeId,
        string? source,
        string? entrypoint,
        IReadOnlyList<CodeFileDto>? files,
        string parameterContext)
    {
        var hasImage = !string.IsNullOrWhiteSpace(image);
        var hasFiles = files is { Count: > 0 };
        var hasCode = !string.IsNullOrWhiteSpace(runtimeId) || !string.IsNullOrWhiteSpace(source) || hasFiles;

        if (hasImage && hasCode)
            throw new ArgumentException(
                $"{parameterContext}: provide either Image or (RuntimeId + code), not both.");

        if (hasImage)
            return new WorkloadSource.ImageWorkload(image!);

        if (hasCode)
        {
            if (string.IsNullOrWhiteSpace(runtimeId))
                throw new ArgumentException($"{parameterContext}: RuntimeId is required for code workloads.");

            if (hasFiles)
            {
                var codeFiles = files!.Select(f => new CodeFile(f.Path, f.Content)).ToList();
                return new WorkloadSource.CodeWorkload(runtimeId!, codeFiles, entrypoint);
            }

            if (string.IsNullOrWhiteSpace(source))
                throw new ArgumentException($"{parameterContext}: Source (or Files) is required for code workloads.");
            return new WorkloadSource.CodeWorkload(runtimeId!, source!, entrypoint);
        }

        return null; // caller decides whether null is acceptable (e.g. optional reduce step)
    }

    /// <summary>Resolve and require a non-null result; throws when neither image nor code provided.</summary>
    public static WorkloadSource ResolveRequired(
        string? image,
        string? runtimeId,
        string? source,
        string? entrypoint,
        IReadOnlyList<CodeFileDto>? files,
        string parameterContext)
    {
        var result = Resolve(image, runtimeId, source, entrypoint, files, parameterContext);
        if (result is null)
            throw new ArgumentException($"{parameterContext}: either Image or (RuntimeId + code) must be provided.");
        return result;
    }
}
