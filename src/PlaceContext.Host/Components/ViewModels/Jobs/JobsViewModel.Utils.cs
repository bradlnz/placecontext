using Microsoft.Extensions.DependencyInjection;
using PlaceContext.Application;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Host.Components.ViewModels.Helpers;
using PlaceContext.Infrastructure.Operations;

namespace PlaceContext.Host.Components.ViewModels;

public sealed partial class JobsViewModel
{
    public bool ParameterNeedsOptions(ParamEdit parameter) =>
        ParameterInputCatalog.Parse(parameter.Type)
            is ParameterInputType.Select
                or ParameterInputType.File;

    public string ParameterOptionsPlaceholder(ParamEdit parameter) =>
        ParameterInputCatalog.Parse(parameter.Type) == ParameterInputType.File
            ? "accepted types, e.g. application/pdf, image/*"
            : "options, comma-separated";

    public bool IsQueuedOrRunning(string? status) =>
        ScopedPresentationCatalog.IsQueuedOrRunning(status);

    public bool IsRunning(string? status) => ScopedPresentationCatalog.IsRunning(status);

    public bool IsScheduleTrigger(string? kind) =>
        ScopedPresentationCatalog.Trigger(kind)
        == PlaceContext.Domain.ValueObjects.TriggerKind.Schedule;

    public bool IsScheduleEditor => IsScheduleTrigger(TrKind);

    public bool IsPostJobActionSelected(PlaceContext.Domain.ValueObjects.PostJobActionKind kind) =>
        EdPostJobActions.Contains(kind);

    public string CurrentEntrypoint =>
        string.IsNullOrEmpty(EdMapEntrypoint)
            ? DefaultEntrypointFor(EdMapRuntimeId)
            : EdMapEntrypoint;

    public string JobStatusAria(bool running, JobView job) =>
        running ? "Running" : WorkloadLabel(job);

    public string JobFacts(JobView job) =>
        $"{job.ShardCount} shard{(job.ShardCount == 1 ? "" : "s")} · concurrency {job.ConcurrencyLimit} · updated {Presentation.Date(job.UpdatedAt.ToWorkspaceTime())}";

    public string OtelTitle(JobRunTelemetry telemetry) =>
        $"{telemetry.Status ?? "running"} · {FormatMs(telemetry.DurationMs)}";

    public string NextRunLabel(TriggerView trigger) =>
        trigger.NextRunAt is { } value ? Presentation.DateTime(value.ToWorkspaceTime()) : "—";

    public string LastFiredLabel(TriggerView trigger) =>
        trigger.LastFiredAt is { } value ? Presentation.DateTime(value.ToWorkspaceTime()) : "never";

    // ── Static helpers used by markup ──────────────────────────────────────────────────────────
    public static string DefaultEntrypointFor(string? runtime) =>
        runtime switch
        {
            "python" => "main.py",
            "go" => "main.go",
            "ruby" => "main.rb",
            "dotnet" => "main.cs",
            _ => "index.js",
        };

    public static string SourcePlaceholderFor(string? runtime) =>
        runtime switch
        {
            "python" =>
                "import sys, json\ndata = json.loads(sys.stdin.read() or \"{}\")\nresult = {}\nprint(json.dumps(result))",
            "go" =>
                "package main\n\nimport (\n    \"encoding/json\"\n    \"io\"\n    \"os\"\n)\n\nfunc main() {\n    in, _ := io.ReadAll(os.Stdin)\n    var data any\n    json.Unmarshal(in, &data)\n    json.NewEncoder(os.Stdout).Encode(map[string]any{})\n}",
            "ruby" =>
                "require 'json'\ndata = JSON.parse(STDIN.read)\nresult = {}\nputs result.to_json",
            "dotnet" =>
                "using System.Text.Json;\nvar input = Console.In.ReadToEnd();\nvar data = JsonSerializer.Deserialize<JsonElement>(input);\nvar result = new { };\nConsole.Write(JsonSerializer.Serialize(result));",
            _ =>
                "const fs = require('fs');\nconst data = JSON.parse(fs.readFileSync('/dev/stdin','utf8'));\nconst result = {};\nprocess.stdout.write(JSON.stringify(result));",
        };

    public static string StatusColor(string? status) => StatusHelper.Color(status);

    public static string StatusBg(string? status) => StatusHelper.Background(status);

    public static string FormatDuration(DateTimeOffset start, DateTimeOffset end) =>
        FormatHelper.Duration(start, end);

    public static string FormatMs(double? ms) => FormatHelper.Ms(ms);

    public static string FormatBytes(long n) => FormatHelper.Bytes(n);

    public static string DataUri(RunArtifactView a) => FormatHelper.DataUri(a);

    public static string PrettyJson(string raw) => FormatHelper.PrettyJson(raw);

    public static readonly (
        PlaceContext.Domain.ValueObjects.PostJobActionKind Kind,
        string Label
    )[] PostJobActionChoices =
    {
        (PlaceContext.Domain.ValueObjects.PostJobActionKind.HtmlReport, "HTML report"),
        (PlaceContext.Domain.ValueObjects.PostJobActionKind.Chart, "Chart"),
        (PlaceContext.Domain.ValueObjects.PostJobActionKind.Csv, "CSV export"),
        (PlaceContext.Domain.ValueObjects.PostJobActionKind.RawBundle, "Raw artifacts bundle"),
    };

    public static readonly (
        PlaceContext.Domain.ValueObjects.JobReturnType Type,
        string Label
    )[] ReturnTypeChoices =
    {
        (PlaceContext.Domain.ValueObjects.JobReturnType.Json, "JSON — stored as result.json"),
        (
            PlaceContext.Domain.ValueObjects.JobReturnType.Table,
            "Table — rendered as an HTML report"
        ),
        (
            PlaceContext.Domain.ValueObjects.JobReturnType.Chart,
            "Chart — rendered as an SVG chart page"
        ),
        (PlaceContext.Domain.ValueObjects.JobReturnType.Html, "HTML — stored openable as-is"),
        (PlaceContext.Domain.ValueObjects.JobReturnType.Csv, "CSV — flattened to a CSV export"),
        (PlaceContext.Domain.ValueObjects.JobReturnType.Text, "Text — stored as result.txt"),
        (
            PlaceContext.Domain.ValueObjects.JobReturnType.Pdf,
            "PDF — file emitted to /out, stored as-is"
        ),
        (
            PlaceContext.Domain.ValueObjects.JobReturnType.Image,
            "Image — file emitted to /out (png/jpg/svg/…)"
        ),
        (
            PlaceContext.Domain.ValueObjects.JobReturnType.Video,
            "Video — file emitted to /out (mp4/webm/…)"
        ),
    };

    public bool IsRunningJob(Guid jobId) => RunningJobId == jobId || PendingRunJobId == jobId;

    // ── Utilities ─────────────────────────────────────────────────────────────────────────────
    public static int[] ParseInts(string raw, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(raw))
            return Array.Empty<int>();
        var result = new List<int>();
        foreach (
            var part in raw.Split(
                ',',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
            )
        )
        {
            if (!int.TryParse(part, out var n))
            {
                error = $"'{part}' is not a valid integer.";
                return Array.Empty<int>();
            }
            result.Add(n);
        }
        return result.ToArray();
    }
}
