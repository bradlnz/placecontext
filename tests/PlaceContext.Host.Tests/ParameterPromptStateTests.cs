using System.Text.Json;
using PlaceContext.Application.Dtos;
using PlaceContext.Host.Components.ViewModels.Helpers;

namespace PlaceContext.Host.Tests;

public sealed class ParameterPromptStateTests
{
    [Fact]
    public void ChainParameterPromptPlan_finds_parameterized_steps_and_prefills_stored_values()
    {
        var first = Job("first");
        var second = Job("second", new JobParameterDto("query", "Search")) with
        {
            InputPayloads = new[] { """{"query":"stored search"}""" },
        };
        var third = Job("third", new JobParameterDto("limit", Required: false));
        var projectId = Guid.NewGuid();
        var chain = new JobChainView(
            Guid.NewGuid(),
            projectId,
            "pipeline",
            null,
            [
                new JobChainStageView([
                    new JobChainStepView(first.Id, first.Name),
                    new JobChainStepView(second.Id, second.Name),
                ]),
                new JobChainStageView([new JobChainStepView(third.Id, third.Name)]),
            ],
            DateTimeOffset.UtcNow
        );

        var plan = ChainParameterPromptPlan.Build(chain, [first, second, third]);

        Assert.Collection(
            plan.Steps,
            step =>
            {
                Assert.Equal(1, step.Index);
                Assert.Equal(second.Id, step.Job.Id);
            },
            step =>
            {
                Assert.Equal(2, step.Index);
                Assert.Equal(third.Id, step.Job.Id);
            }
        );
        Assert.Equal("stored search", plan.Defaults["step1:query"]);
        Assert.Equal("", plan.Defaults["step2:limit"]);
        Assert.DoesNotContain("step0", plan.Defaults.Keys);
    }

    [Fact]
    public void ChainArgKey_is_ui_disambiguator_only()
    {
        Assert.Equal("step0:address", ParameterPromptState.ChainArgKey(0, "address"));
        Assert.Equal("step13:limit", ParameterPromptState.ChainArgKey(13, "limit"));
    }

    [Fact]
    public void ToJobPayload_uses_bare_param_names()
    {
        var prompt = new ParameterPromptState();
        prompt.Reset(new Dictionary<string, string> { ["address"] = "123 Main", ["limit"] = "10" });
        var parameters = new[]
        {
            new JobParameterDto("address"),
            new JobParameterDto("limit", Required: false),
        };

        var json = prompt.ToJobPayload(parameters);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("123 Main", doc.RootElement.GetProperty("address").GetString());
        Assert.Equal("10", doc.RootElement.GetProperty("limit").GetString());
        Assert.False(doc.RootElement.TryGetProperty("step0:address", out _));
    }

    [Fact]
    public void ToJobPayload_serializes_file_marker_as_an_object()
    {
        var marker = """
            {"$file":{"bucket":"reports","key":"job-inputs/project/plan.pdf","filename":"plan.pdf"}}
            """;
        var prompt = new ParameterPromptState();
        prompt.Set("source_file", marker);

        var json = prompt.ToJobPayload(new[] { new JobParameterDto("source_file", Type: "file") });

        using var document = JsonDocument.Parse(json);
        var file = document.RootElement.GetProperty("source_file").GetProperty("$file");
        Assert.Equal("plan.pdf", file.GetProperty("filename").GetString());
        Assert.Equal(
            JsonValueKind.Object,
            document.RootElement.GetProperty("source_file").ValueKind
        );
    }

    [Fact]
    public void ToStepPayloadOverrides_groups_by_step_with_bare_param_names()
    {
        var prompt = new ParameterPromptState();
        prompt.Reset(
            new Dictionary<string, string>
            {
                ["step0:address"] = "123 Main",
                ["step0:city"] = "Austin",
                ["step2:query"] = "foo",
                ["step13:limit"] = "10",
            }
        );

        var steps = new List<(int Index, JobView Job)>
        {
            (
                0,
                Job(
                    "a",
                    new JobParameterDto("address"),
                    new JobParameterDto("city", Required: false)
                )
            ),
            (2, Job("b", new JobParameterDto("query"))),
            (13, Job("c", new JobParameterDto("limit", Required: false))),
        };

        var overrides = prompt.ToStepPayloadOverrides(steps);

        Assert.Equal(3, overrides.Count);
        Assert.Equal("""{"address":"123 Main","city":"Austin"}""", overrides[0]);
        Assert.Equal("""{"query":"foo"}""", overrides[2]);
        Assert.Equal("""{"limit":"10"}""", overrides[13]);

        // Must not leak UI keys onto the wire
        foreach (var json in overrides.Values)
        {
            using var doc = JsonDocument.Parse(json);
            foreach (var prop in doc.RootElement.EnumerateObject())
                Assert.DoesNotContain(':', prop.Name);
        }
    }

    [Fact]
    public void ToStepPayloadOverrides_skips_steps_without_parameters()
    {
        var prompt = new ParameterPromptState();
        var steps = new List<(int Index, JobView Job)> { (1, Job("noop")) };
        Assert.Empty(prompt.ToStepPayloadOverrides(steps));
    }

    [Fact]
    public void ValidateChainStepParameters_reports_step_labels()
    {
        var prompt = new ParameterPromptState();
        prompt.Clear();
        var steps = new List<(int Index, JobView Job)>
        {
            (0, Job("a", new JobParameterDto("address", "Street"))),
        };

        Assert.False(prompt.ValidateChainStepParameters(steps));
        Assert.Equal("Required: step 1: Street", prompt.Error);
    }

    [Fact]
    public void ValidateJobParameters_lists_missing_required()
    {
        var prompt = new ParameterPromptState();
        prompt.Set("optional", "x");
        var parameters = new[]
        {
            new JobParameterDto("name", "Name"),
            new JobParameterDto("optional", Required: false),
        };

        Assert.False(prompt.ValidateJobParameters(parameters));
        Assert.Equal("Required: Name", prompt.Error);
    }

    private static JobView Job(string name, params JobParameterDto[] parameters) =>
        new(
            Id: Guid.NewGuid(),
            ProjectId: Guid.NewGuid(),
            Name: name,
            Description: null,
            MapSourceKind: "code",
            MapImage: null,
            MapRuntimeId: "node",
            MapSource: "",
            MapEntrypoint: "index.js",
            MapFiles: Array.Empty<CodeFileDto>(),
            ShardCount: 1,
            InputPayloads: Array.Empty<string>(),
            MapEnv: new Dictionary<string, string>(),
            ReduceSourceKind: null,
            ReduceImage: null,
            ReduceRuntimeId: null,
            ReduceSource: null,
            ReduceEntrypoint: null,
            ReduceFiles: Array.Empty<CodeFileDto>(),
            ReduceEnv: null,
            ConcurrencyLimit: 1,
            SuccessExitCodes: new[] { 0 },
            PartialExitCodes: Array.Empty<int>(),
            AllowNetworkEgress: false,
            AllowApiInvocation: false,
            Parameters: parameters,
            PostJobActions: Array.Empty<PlaceContext.Domain.ValueObjects.PostJobActionKind>(),
            ReturnType: PlaceContext.Domain.ValueObjects.JobReturnType.Json,
            ReturnFileName: null,
            RetryCount: 0,
            RetryDelaySeconds: 0,
            McpConnectionIds: Array.Empty<Guid>(),
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow
        );
}
