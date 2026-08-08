namespace PlaceContext.Domain.ValueObjects;

/// <summary>Inline source code executed inside a registered runtime sandbox.</summary>
public sealed class CodeWorkload : WorkloadSource
{
    private const string DefaultSingleFilePath = "main";

    public CodeWorkload(string runtimeId, IReadOnlyList<CodeFile> files, string? entrypoint)
    {
        if (string.IsNullOrWhiteSpace(runtimeId))
            throw new ArgumentException("RuntimeId must not be empty.", nameof(runtimeId));
        ArgumentNullException.ThrowIfNull(files);
        if (files.Count == 0)
            throw new ArgumentException("At least one file is required for a code workload.", nameof(files));

        var paths = files.Select(file => file.Path).ToList();
        if (paths.Distinct(StringComparer.Ordinal).Count() != paths.Count)
            throw new ArgumentException("File paths must be unique within a code workload.", nameof(files));

        RuntimeId = runtimeId.Trim();
        Files = files;
        Entrypoint = string.IsNullOrWhiteSpace(entrypoint) ? null : entrypoint.Trim();

        if (Entrypoint is null && files.Count > 1)
            throw new ArgumentException(
                "An entrypoint is required when multiple files are provided.",
                nameof(entrypoint));
        if (Entrypoint is not null && !paths.Contains(Entrypoint, StringComparer.Ordinal))
            throw new ArgumentException(
                $"Entrypoint '{Entrypoint}' must match one of the supplied file paths.",
                nameof(entrypoint));
    }

    public CodeWorkload(string runtimeId, string source, string? entrypoint)
        : this(
            runtimeId,
            [new CodeFile(
                string.IsNullOrWhiteSpace(entrypoint) ? DefaultSingleFilePath : entrypoint,
                RequireSource(source))],
            entrypoint)
    {
    }

    public string RuntimeId { get; }
    public IReadOnlyList<CodeFile> Files { get; }
    public string? Entrypoint { get; }
    public CodeFile EntryFile => Entrypoint is null
        ? Files[0]
        : Files.First(file => file.Path == Entrypoint);
    public string Source => EntryFile.Content;
    public override string Label => RuntimeId;

    private static string RequireSource(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
            throw new ArgumentException("Source must not be empty.", nameof(source));

        return source;
    }
}
