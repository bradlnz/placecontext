namespace PlaceContext.Domain.ValueObjects;

/// <summary>
/// Discriminated union: describes how a workload step (map shard or reduce) is executed.
/// Either a pre-built container image or inline source code to be run inside a generic runtime sandbox.
/// All fields are generic — no domain knowledge lives here.
/// </summary>
public abstract class WorkloadSource
{
    private WorkloadSource() { }

    /// <summary>Returns the display label for the source (image name or runtimeId).</summary>
    public abstract string Label { get; }

    // ── Variant: pre-built container image ───────────────────────────────────────────────────────

    /// <summary>Run a pre-built container image (existing behaviour).</summary>
    public sealed class ImageWorkload : WorkloadSource
    {
        public ImageWorkload(string image)
        {
            if (string.IsNullOrWhiteSpace(image))
                throw new ArgumentException("Image must not be empty.", nameof(image));
            Image = image.Trim();
        }

        /// <summary>Container image reference (e.g. "myorg/worker:latest").</summary>
        public string Image { get; }

        public override string Label => Image;
    }

    // ── Variant: inline source code run inside a runtime sandbox ─────────────────────────────────

    /// <summary>
    /// Inline source code run inside a generic runtime sandbox container.
    /// <para>
    /// The runtime sandbox is resolved from the runtime registry (Infrastructure options) by
    /// <paramref name="RuntimeId"/>. The registry maps e.g. "node" → node:22-slim + invoke command.
    /// Source is materialised to a temp dir and mounted read-only at /work inside the sandbox.
    /// The same opaque stdin/stdout/artifact contract as ImageWorkload applies.
    /// </para>
    /// </summary>
    public sealed class CodeWorkload : WorkloadSource
    {
        /// <summary>Placeholder path for a single-file workload created with no explicit entrypoint.</summary>
        private const string DefaultSingleFilePath = "main";

        /// <summary>
        /// Multi-file constructor: a set of source files plus the entry-point path.
        /// <para>
        /// When <paramref name="entrypoint"/> is null exactly one file must be supplied and the runtime's
        /// default entrypoint name is used at run-time (the file's <see cref="CodeFile.Path"/> is ignored).
        /// When <paramref name="entrypoint"/> is set it must match one of the files' paths.
        /// </para>
        /// </summary>
        public CodeWorkload(string runtimeId, IReadOnlyList<CodeFile> files, string? entrypoint)
        {
            if (string.IsNullOrWhiteSpace(runtimeId))
                throw new ArgumentException("RuntimeId must not be empty.", nameof(runtimeId));
            ArgumentNullException.ThrowIfNull(files);
            if (files.Count == 0)
                throw new ArgumentException("At least one file is required for a code workload.", nameof(files));

            var paths = files.Select(f => f.Path).ToList();
            if (paths.Distinct(StringComparer.Ordinal).Count() != paths.Count)
                throw new ArgumentException("File paths must be unique within a code workload.", nameof(files));

            RuntimeId = runtimeId.Trim();
            Files = files;
            Entrypoint = string.IsNullOrWhiteSpace(entrypoint) ? null : entrypoint.Trim();

            if (Entrypoint is null && files.Count > 1)
                throw new ArgumentException("An entrypoint is required when multiple files are provided.", nameof(entrypoint));
            if (Entrypoint is not null && !paths.Contains(Entrypoint, StringComparer.Ordinal))
                throw new ArgumentException($"Entrypoint '{Entrypoint}' must match one of the supplied file paths.", nameof(entrypoint));
        }

        /// <summary>
        /// Back-compat single-file constructor. Wraps <paramref name="source"/> into a one-file set.
        /// When <paramref name="entrypoint"/> is provided it becomes the file's path; otherwise a
        /// placeholder path is stored and the runtime default entrypoint is used at run-time.
        /// </summary>
        public CodeWorkload(string runtimeId, string source, string? entrypoint)
            : this(
                runtimeId,
                new[] { new CodeFile(string.IsNullOrWhiteSpace(entrypoint) ? DefaultSingleFilePath : entrypoint!, RequireSource(source)) },
                entrypoint)
        { }

        /// <summary>Identifier of the runtime sandbox (e.g. "node", "python").</summary>
        public string RuntimeId { get; }

        /// <summary>
        /// The source files that make up this workload, materialised under /work preserving their paths.
        /// PlaceContext does not parse or interpret them.
        /// </summary>
        public IReadOnlyList<CodeFile> Files { get; }

        /// <summary>
        /// Path of the entry-point file within the work directory (e.g. "index.js", "lib/main.py").
        /// When null the runtime's default entrypoint is used (only valid for a single-file workload).
        /// </summary>
        public string? Entrypoint { get; }

        /// <summary>The entry-point file, resolved from <see cref="Entrypoint"/> (or the sole file when null).</summary>
        public CodeFile EntryFile => Entrypoint is null
            ? Files[0]
            : Files.First(f => f.Path == Entrypoint);

        /// <summary>
        /// Convenience accessor: content of the entry-point file. For single-file workloads this is the
        /// whole source; for multi-file workloads it is the entry file's content.
        /// </summary>
        public string Source => EntryFile.Content;

        public override string Label => RuntimeId;

        private static string RequireSource(string source)
        {
            if (string.IsNullOrWhiteSpace(source))
                throw new ArgumentException("Source must not be empty.", nameof(source));
            return source;
        }
    }
}
