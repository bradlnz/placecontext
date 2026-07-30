using System.IO.Compression;
using System.Text;
using System.Text.Json;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Host.Backup;

/// <summary>
/// Builds a portable source-code archive from the same tenant-scoped manifest used by backup export.
/// Environment values are intentionally excluded from the per-job metadata.
/// </summary>
public static class JobsCodeArchiveBuilder
{
    private const string Root = "placecontext-jobs";
    private static readonly UTF8Encoding Utf8WithoutBom = new(false);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public static byte[] Build(BackupManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            var projects = manifest.Projects.ToDictionary(project => project.ProjectId);
            var usedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            WriteText(
                archive,
                $"{Root}/README.md",
                BuildReadme(manifest, projects),
                manifest.ExportedAt,
                usedPaths);

            foreach (var job in manifest.Jobs
                         .OrderBy(job => projects.GetValueOrDefault(job.ProjectId)?.Name, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(job => job.Name, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(job => job.JobId))
            {
                projects.TryGetValue(job.ProjectId, out var project);
                var projectName = project?.Name ?? "Unknown project";
                var jobRoot =
                    $"{Root}/{FolderName(projectName, job.ProjectId)}/{FolderName(job.Name, job.JobId)}";

                WriteText(
                    archive,
                    $"{jobRoot}/job.json",
                    BuildJobMetadata(job, project),
                    manifest.ExportedAt,
                    usedPaths);

                WriteSourceFiles(
                    archive,
                    $"{jobRoot}/map",
                    job.MapFiles,
                    manifest.ExportedAt,
                    usedPaths);

                WriteSourceFiles(
                    archive,
                    $"{jobRoot}/reduce",
                    job.ReduceFiles,
                    manifest.ExportedAt,
                    usedPaths);
            }
        }

        return output.ToArray();
    }

    private static string BuildReadme(
        BackupManifest manifest,
        IReadOnlyDictionary<Guid, ProjectManifest> projects)
    {
        var codeFileCount = manifest.Jobs.Sum(job => job.MapFiles.Count + job.ReduceFiles.Count);
        var imageWorkloadCount = manifest.Jobs.Count(job =>
            string.Equals(job.MapSourceKind, "image", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(job.ReduceSourceKind, "image", StringComparison.OrdinalIgnoreCase));

        var builder = new StringBuilder()
            .AppendLine("# PlaceContext job code export")
            .AppendLine()
            .AppendLine($"Exported: {manifest.ExportedAt:O}")
            .AppendLine($"Projects: {projects.Count}")
            .AppendLine($"Jobs: {manifest.Jobs.Count}")
            .AppendLine($"Source files: {codeFileCount}")
            .AppendLine()
            .AppendLine("Each project contains one folder per job. Code-backed map and reduce workloads")
            .AppendLine("are stored in `map/` and `reduce/`; `job.json` records their runtime and entrypoint.")
            .AppendLine()
            .AppendLine("Environment values and vault secrets are intentionally excluded.");

        if (imageWorkloadCount > 0)
        {
            builder
                .AppendLine()
                .AppendLine($"{imageWorkloadCount} job(s) use at least one image-backed workload. Those")
                .AppendLine("workloads have no source files to export, so only their image metadata appears.");
        }

        return builder.ToString();
    }

    private static string BuildJobMetadata(JobManifest job, ProjectManifest? project) =>
        JsonSerializer.Serialize(
            new
            {
                jobId = job.JobId,
                projectId = job.ProjectId,
                projectName = project?.Name,
                projectPath = project?.Path,
                job.Name,
                job.Description,
                map = new
                {
                    sourceKind = job.MapSourceKind,
                    image = job.MapImage,
                    runtimeId = job.MapRuntimeId,
                    entrypoint = job.MapEntrypoint,
                },
                reduce = job.ReduceSourceKind is null
                    ? null
                    : new
                    {
                        sourceKind = job.ReduceSourceKind,
                        image = job.ReduceImage,
                        runtimeId = job.ReduceRuntimeId,
                        entrypoint = job.ReduceEntrypoint,
                    },
            },
            Json);

    private static void WriteSourceFiles(
        ZipArchive archive,
        string workloadRoot,
        IReadOnlyList<CodeFileDto> files,
        DateTimeOffset exportedAt,
        ISet<string> usedPaths)
    {
        foreach (var file in files.OrderBy(file => file.Path, StringComparer.OrdinalIgnoreCase))
        {
            var relativePath = SafeRelativePath(file.Path);
            WriteText(archive, $"{workloadRoot}/{relativePath}", file.Content, exportedAt, usedPaths);
        }
    }

    private static void WriteText(
        ZipArchive archive,
        string requestedPath,
        string content,
        DateTimeOffset exportedAt,
        ISet<string> usedPaths)
    {
        var path = UniquePath(requestedPath, usedPaths);
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        entry.LastWriteTime = ClampZipTimestamp(exportedAt);

        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, Utf8WithoutBom);
        writer.Write(content);
    }

    private static string FolderName(string name, Guid id)
    {
        var safeName = SafeSegment(name);
        return $"{safeName}--{id:N}"[..(safeName.Length + 10)];
    }

    private static string SafeRelativePath(string path)
    {
        var segments = path
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Where(segment => segment != ".")
            .Select(segment => segment == ".." ? "_" : SafeSegment(segment))
            .ToArray();

        return segments.Length == 0 ? "source.txt" : string.Join('/', segments);
    }

    private static string SafeSegment(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value.Trim())
        {
            builder.Append(character < ' ' || character is '<' or '>' or ':' or '"' or '/' or '\\' or '|' or '?' or '*'
                ? '-'
                : character);
        }

        var segment = builder.ToString().TrimEnd(' ', '.');
        if (string.IsNullOrWhiteSpace(segment))
            segment = "unnamed";

        if (IsWindowsDeviceName(segment))
            segment = $"_{segment}";

        return segment.Length <= 80 ? segment : segment[..80];
    }

    private static bool IsWindowsDeviceName(string segment)
    {
        var baseName = segment.Split('.')[0];
        return baseName.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
               baseName.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
               baseName.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
               baseName.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
               (baseName.Length == 4 &&
                (baseName.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
                 baseName.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) &&
                baseName[3] is >= '1' and <= '9');
    }

    private static string UniquePath(string requestedPath, ISet<string> usedPaths)
    {
        if (usedPaths.Add(requestedPath))
            return requestedPath;

        var slash = requestedPath.LastIndexOf('/');
        var directory = slash >= 0 ? requestedPath[..(slash + 1)] : "";
        var fileName = slash >= 0 ? requestedPath[(slash + 1)..] : requestedPath;
        var dot = fileName.LastIndexOf('.');
        var stem = dot > 0 ? fileName[..dot] : fileName;
        var extension = dot > 0 ? fileName[dot..] : "";

        for (var suffix = 2; ; suffix++)
        {
            var candidate = $"{directory}{stem}-{suffix}{extension}";
            if (usedPaths.Add(candidate))
                return candidate;
        }
    }

    private static DateTimeOffset ClampZipTimestamp(DateTimeOffset timestamp)
    {
        var utc = timestamp.ToUniversalTime();
        var minimum = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var maximum = new DateTimeOffset(2107, 12, 31, 23, 59, 58, TimeSpan.Zero);
        return utc < minimum ? minimum : utc > maximum ? maximum : utc;
    }
}
