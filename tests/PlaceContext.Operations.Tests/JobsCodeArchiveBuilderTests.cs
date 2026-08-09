using System.IO.Compression;
using System.Text;
using PlaceContext.Application.Dtos;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.Operations.Backup;

namespace PlaceContext.Operations.Tests;

public class JobsCodeArchiveBuilderTests
{
    [Fact]
    public void Builds_project_and_job_folders_with_map_and_reduce_source()
    {
        var projectId = Guid.NewGuid();
        var codeJobId = Guid.NewGuid();
        var imageJobId = Guid.NewGuid();
        var manifest = Manifest(
            new ProjectManifest(projectId, "Analytics / Prod", "/analytics"),
            CodeJob(projectId, codeJobId),
            ImageJob(projectId, imageJobId)
        );

        var bytes = JobsCodeArchiveBuilder.Build(manifest);

        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        var entries = archive.Entries.ToDictionary(entry => entry.FullName);

        Assert.Contains("placecontext-jobs/README.md", entries.Keys);
        var projectFolder = $"Analytics - Prod--{projectId.ToString("N")[..8]}";
        var codeJobFolder = $"Daily- Revenue--{codeJobId.ToString("N")[..8]}";
        var codeJobRoot = $"placecontext-jobs/{projectFolder}/{codeJobFolder}";
        Assert.Contains($"{codeJobRoot}/map/src/main.py", entries.Keys);
        Assert.Contains($"{codeJobRoot}/map/lib/helpers.py", entries.Keys);
        Assert.Contains($"{codeJobRoot}/reduce/reduce.py", entries.Keys);

        var metadata = Read(entries[$"{codeJobRoot}/job.json"]);
        Assert.Contains("\"runtimeId\": \"python\"", metadata);
        Assert.DoesNotContain("super-secret", metadata);

        var imageJobFolder = $"Container only--{imageJobId.ToString("N")[..8]}";
        var imageJobRoot = $"placecontext-jobs/{projectFolder}/{imageJobFolder}";
        Assert.Contains($"{imageJobRoot}/job.json", entries.Keys);
        Assert.DoesNotContain(
            entries.Keys,
            path => path.StartsWith($"{imageJobRoot}/map/", StringComparison.Ordinal)
        );
        Assert.Contains(
            "\"image\": \"example/worker:latest\"",
            Read(entries[$"{imageJobRoot}/job.json"])
        );
    }

    [Fact]
    public void Keeps_untrusted_source_paths_inside_the_job_folder_and_makes_collisions_unique()
    {
        var projectId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var job = CodeJob(
            projectId,
            jobId,
            [
                new CodeFileDto("../../token.txt", "first"),
                new CodeFileDto("a:b/token.txt", "second"),
                new CodeFileDto("a?b/token.txt", "third"),
            ]
        );

        var bytes = JobsCodeArchiveBuilder.Build(
            Manifest(new ProjectManifest(projectId, "Project", "/project"), job)
        );

        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        Assert.DoesNotContain(
            archive.Entries,
            entry => entry.FullName.Contains("../", StringComparison.Ordinal)
        );
        Assert.Equal(
            archive.Entries.Count,
            archive
                .Entries.Select(entry => entry.FullName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count()
        );
        Assert.Contains(
            archive.Entries,
            entry => entry.FullName.EndsWith("/map/_/_/token.txt", StringComparison.Ordinal)
        );
        Assert.Contains(
            archive.Entries,
            entry => entry.FullName.EndsWith("/map/a-b/token.txt", StringComparison.Ordinal)
        );
        Assert.Contains(
            archive.Entries,
            entry => entry.FullName.EndsWith("/map/a-b/token-2.txt", StringComparison.Ordinal)
        );
    }

    private static BackupManifest Manifest(ProjectManifest project, params JobManifest[] jobs) =>
        new(
            BackupManifest.CurrentSchemaVersion,
            new DateTimeOffset(2026, 7, 30, 1, 2, 3, TimeSpan.Zero),
            new TenantSettingsManifest("UTC", null, null),
            [project],
            jobs,
            [],
            [],
            [],
            []
        );

    private static JobManifest CodeJob(
        Guid projectId,
        Guid jobId,
        IReadOnlyList<CodeFileDto>? mapFiles = null
    ) =>
        new(
            jobId,
            projectId,
            "Daily: Revenue",
            "Build the daily report",
            "code",
            null,
            "python",
            "src/main.py",
            mapFiles
                ??
                [
                    new CodeFileDto("src/main.py", "print('map')"),
                    new CodeFileDto("lib/helpers.py", "def helper(): pass"),
                ],
            [],
            new Dictionary<string, string> { ["API_KEY"] = "super-secret" },
            "code",
            null,
            "python",
            "reduce.py",
            [new CodeFileDto("reduce.py", "print('reduce')")],
            new Dictionary<string, string> { ["TOKEN"] = "super-secret" },
            1,
            [0],
            [],
            false,
            false,
            300,
            [],
            [],
            JobReturnType.Json,
            null
        );

    private static JobManifest ImageJob(Guid projectId, Guid jobId) =>
        new(
            jobId,
            projectId,
            "Container only",
            null,
            "image",
            "example/worker:latest",
            null,
            null,
            [],
            [],
            new Dictionary<string, string> { ["TOKEN"] = "super-secret" },
            null,
            null,
            null,
            null,
            [],
            null,
            1,
            [0],
            [],
            false,
            false,
            300,
            [],
            [],
            JobReturnType.Json,
            null
        );

    private static string Read(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
