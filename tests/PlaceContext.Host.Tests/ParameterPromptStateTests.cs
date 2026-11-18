using System.Text.Json;
using PlaceContext.Application.Dtos;
using PlaceContext.Host.Components.ViewModels.Helpers;

namespace PlaceContext.Host.Tests;

public sealed class ParameterPromptStateTests
{
    [Fact]
    public void Workflow_inputs_follow_nested_entry_branches_without_prompting_for_downstream_outputs()
    {
        var job = Job("Intake", new("request_id", Type: "uuid"), new("question"))
            with { InputPayloads = ["{\"question\":\"Example\"}"] };
        var later = Job("Later", new JobParameterDto("generated_output"));
        var child = new JobChainView(Guid.NewGuid(), job.ProjectId, "Child", null,
            [new([new(job.Id, job.Name)]), new([new(later.Id, later.Name)])], DateTimeOffset.UtcNow);
        var nested = child with { Id = Guid.NewGuid(), Stages = [new([], Action: new SubWorkflowChainActionView(child.Id, child.Name))] };
        var root = child with { Id = Guid.NewGuid(), Stages = [new([], Action: new ParallelWorkflowsChainActionView(
            [new(nested.Id, nested.Name, "true"), new(child.Id, child.Name, "false", WhenFalse: true)]))] };
        var plan = ChainParameterPromptPlan.Build(root, [job, later], [root, nested, child]);
        Assert.Empty(plan.Steps);
        Assert.Equal(new[] { "request_id", "question" }, plan.Inputs.Select(input => input.Parameter.Name));
        Assert.True(Guid.TryParse(plan.Inputs[0].Value, out _));
        Assert.Equal("Example", plan.Inputs[1].Value);
        Assert.All(plan.Inputs, input => Assert.True(input.Parameter.Required));
        Assert.Empty(ChainParameterPromptPlan.Build(root, [job], [child with { ProjectId = Guid.NewGuid() }]).Inputs);
        var cycle = child with { Stages = [new([], Action: new SubWorkflowChainActionView(root.Id, root.Name))] };
        Assert.Empty(ChainParameterPromptPlan.Build(root, [job], [root, nested, cycle]).Inputs);
    }

    [Fact]
    public void Workflow_inputs_reject_conflicting_types_across_entry_branches()
    {
        var text = Job("Text", new JobParameterDto("value", Type: "text"));
        var number = Job("Number", new JobParameterDto("value", Type: "integer"));
        var first = new JobChainView(Guid.NewGuid(), text.ProjectId, "First", null,
            [new([new(text.Id, text.Name)])], DateTimeOffset.UtcNow);
        var second = first with { Id = Guid.NewGuid(), Stages = [new([new(number.Id, number.Name)])] };
        var root = first with { Id = Guid.NewGuid(), Stages = [new([], Action: new ParallelWorkflowsChainActionView(
            [new(first.Id, first.Name, "true"), new(second.Id, second.Name, "true")]))] };
        var error = Assert.Throws<ArgumentException>(() => ChainParameterPromptPlan.Build(root, [text, number], [first, second]));
        Assert.Contains("different types for input 'value'", error.Message);
    }

    [Fact]
    public void Workflow_form_infers_first_step_examples_and_preserves_structured_and_typed_values()
    {
        var job = Job("Input") with { InputPayloads = ["""{"request_id":"c6b8fef6-36b4-43db-a7b6-806873e05db5","count":12,"enabled":false,"data":{"name":"Ada"},"items":[1,2],"optional":null}"""] };
        var downstream = Job("Later") with { InputPayloads = ["""{"do_not_override":"upstream output"}"""] };
        var chain = new JobChainView(Guid.NewGuid(), job.ProjectId, "Test", null,
            [new([new(job.Id, job.Name)]), new([new(downstream.Id, downstream.Name)])], DateTimeOffset.UtcNow);
        var plan = ChainParameterPromptPlan.Build(chain, [job, downstream]);
        var step = Assert.Single(plan.Steps);
        Assert.Empty(job.Parameters);
        Assert.Equal("uuid", step.Job.Parameters.Single(p => p.Name == "request_id").Type);
        var prompt = new ParameterPromptState(); prompt.Reset(plan.Defaults);
        Assert.True(prompt.ValidateChainStepParameters(plan.Steps));
        using var actual = JsonDocument.Parse(prompt.ToStepPayloadOverrides(plan.Steps)[0]);
        Assert.Equal(12, actual.RootElement.GetProperty("count").GetInt32());
        Assert.False(actual.RootElement.GetProperty("enabled").GetBoolean());
        Assert.Equal("Ada", actual.RootElement.GetProperty("data").GetProperty("name").GetString());
        Assert.Equal(2, actual.RootElement.GetProperty("items").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, actual.RootElement.GetProperty("optional").ValueKind);
    }

    [Fact]
    public void Workflow_defaults_generate_only_missing_identifiers_and_keep_provided_values()
    {
        var existing = Guid.NewGuid().ToString();
        var job = Job("Input", new("request_id", Type: "uuid"), new("record_id", Type: "integer"),
            new("count", Type: "integer"), new("approved", Type: "checkbox"), new("existing", Type: "uuid"))
            with { InputPayloads = [System.Text.Json.JsonSerializer.Serialize(new { existing, request_id = (string?)null, record_id = (int?)null })] };
        var chain = new JobChainView(Guid.NewGuid(), job.ProjectId, "Test", null, [new([new(job.Id, job.Name)])], DateTimeOffset.UtcNow);
        var plan = ChainParameterPromptPlan.Build(chain, [job]);
        Assert.True(Guid.TryParse(plan.Defaults["step0:request_id"], out _));
        Assert.True(int.Parse(plan.Defaults["step0:record_id"]) > 0);
        Assert.Equal("", plan.Defaults["step0:count"]);
        Assert.Equal("false", plan.Defaults["step0:approved"]);
        Assert.Equal(existing, plan.Defaults["step0:existing"]);
        Assert.NotEqual(plan.Defaults["step0:request_id"], ChainParameterPromptPlan.Build(chain, [job]).Defaults["step0:request_id"]);
    }

    [Theory]
    [InlineData("uuid", "not-a-uuid")]
    [InlineData("integer", "1.5")]
    [InlineData("integer", "9223372036854775808")]
    [InlineData("number", "NaN")]
    [InlineData("checkbox", "yes")]
    [InlineData("json", "{bad")]
    public void Invalid_typed_run_values_are_rejected_without_losing_the_draft(string type, string value)
    {
        var prompt = new ParameterPromptState(); prompt.Set("input", value);
        Assert.False(prompt.ValidateJobParameters([new("input", Type: type)]));
        Assert.NotNull(prompt.Error); Assert.Equal(value, prompt.Get("input"));
        Assert.Contains(value.Replace("\"", "\\\""), prompt.ToJobPayload([new("input", Type: type)]));
    }

    [Fact]
    public void Numbers_and_booleans_are_not_strings_in_run_payloads()
    {
        var prompt = new ParameterPromptState(); prompt.Set("number", "12.75"); prompt.Set("integer", "2147483647"); prompt.Set("flag", "false");
        JobParameterDto[] fields = [new("number", Type: "number"), new("integer", Type: "integer"), new("flag", Type: "checkbox")];
        Assert.True(prompt.ValidateJobParameters(fields));
        using var json = JsonDocument.Parse(prompt.ToJobPayload(fields));
        Assert.Equal(12.75m, json.RootElement.GetProperty("number").GetDecimal());
        Assert.Equal(2147483647, json.RootElement.GetProperty("integer").GetInt64());
        Assert.False(json.RootElement.GetProperty("flag").GetBoolean());
    }

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
    public void Chain_attachment_merges_into_the_initial_payload_as_a_file_object()
    {
        var marker = """
            {"$file":{"bucket":"reports","key":"job-inputs/project/plan.pdf","filename":"plan.pdf"}}
            """;

        var json = ParameterPromptState.WithChainAttachment(
            """{"customer":"Acme"}""",
            marker
        );

        using var document = JsonDocument.Parse(json!);
        Assert.Equal("Acme", document.RootElement.GetProperty("customer").GetString());
        var attachment = document.RootElement.GetProperty("attachment").GetProperty("$file");
        Assert.Equal("plan.pdf", attachment.GetProperty("filename").GetString());
    }

    [Fact]
    public void Chain_attachment_is_optional_and_ignores_invalid_markers()
    {
        const string payload = """{"customer":"Acme"}""";

        Assert.Equal(payload, ParameterPromptState.WithChainAttachment(payload, ""));
        Assert.Equal(payload, ParameterPromptState.WithChainAttachment(payload, "not a marker"));
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
            {
                Assert.DoesNotContain(':', prop.Name);
            }
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
