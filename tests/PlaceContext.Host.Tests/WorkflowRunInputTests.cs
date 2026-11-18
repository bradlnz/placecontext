using System.Text.Json;
using Microsoft.AspNetCore.Components;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Jobs;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.Host.Components.ViewModels;

namespace PlaceContext.Host.Tests;

public sealed class WorkflowRunInputTests
{
    [Theory]
    [InlineData(LoopChainAction.TextResponse)]
    [InlineData(LoopChainAction.YesNoResponse)]
    public async Task Human_request_opens_a_modal_and_keeps_its_draft_when_dismissed(string responseType)
    {
        var vm = new JobChainsViewModel(null!, null!, null!, new Navigation());
        var project = Guid.NewGuid();
        vm.Initialize(project);
        var request = new ChainInputRequest(Guid.NewGuid(), [new(0, "Review", [new("answer")])],
            Kind: LoopChainAction.HumanType, ResponseType: responseType);
        var run = new ChainRunView(Guid.NewGuid(), Guid.NewGuid(), "Review", "AwaitingInput", [], null, DateTimeOffset.UtcNow, null)
            { ProjectId = project, InputRequest = request };
        try
        {
            await vm.OpenChainRunAsync(run);
            Assert.True(vm.ShowHumanInput);
            _ = vm.GetResumeArg(0, "answer");
            vm.SetResumeArg(0, "answer", "Review draft");
            vm.CloseHumanInput();
            Assert.False(vm.ShowHumanInput);
            await vm.OpenChainRunAsync(run);
            Assert.False(vm.ShowHumanInput);
            vm.OpenHumanInput();
            Assert.True(vm.ShowHumanInput);
            Assert.Equal("Review draft", vm.GetResumeArg(0, "answer"));
            Assert.Equal("AwaitingInput", vm.OpenRun!.Status);
            vm.CloseHumanInput();
            await vm.OpenChainRunAsync(run with { InputRequest = request with { Id = Guid.NewGuid() } });
            Assert.True(vm.ShowHumanInput);
            await vm.OpenChainRunAsync(run with { InputRequest = request with { SubmittedValues = "{}" } });
            Assert.False(vm.ShowHumanInput);
            await vm.OpenChainRunAsync(run with { InputRequest = request with { Kind = LoopChainAction.AgentType } });
            Assert.False(vm.ShowHumanInput);
            await vm.OpenChainRunAsync(run with { Status = "Succeeded" });
            Assert.False(vm.ShowHumanInput);
        }
        finally { vm.DetachEvents(); }
    }

    [Fact]
    public async Task Discovered_required_workflow_fields_validate_and_submit_as_initial_input()
    {
        var queue = new Queue();
        var vm = new JobChainsViewModel(null!, null!, null!, new Navigation(), submissions: queue);
        await vm.RunChainAsync(Workflow());
        vm.RunInputFields.Clear();
        var field = new JobChainsViewModel.RunInputField { Name = "question", Definition = new("question", "Question") };
        vm.RunInputFields.Add(field);
        vm.RemoveRunInputField(field);
        Assert.Single(vm.RunInputFields);
        await vm.SubmitRunPromptAsync();
        Assert.NotNull(vm.RunPromptError);
        Assert.Equal(0, queue.Count);
        field.Value = "Help me sign in";
        await vm.SubmitRunPromptAsync();
        Assert.Equal(1, queue.Count);
        using var data = JsonDocument.Parse(queue.Payload!);
        Assert.Equal(field.Value, data.RootElement.GetProperty("question").GetString());
    }

    internal static JobView InputJob(Guid projectId, bool declared) => new(
        Guid.NewGuid(), projectId, "Question", null, "code", null, "node", "", "index.js", [], 1,
        ["""{"question":"Example question","count":3,"enabled":false}"""], new Dictionary<string, string>(),
        null, null, null, null, null, [], null, 1, [0], [], false, false,
        declared ? [new("question", "Question"), new("count", Type: "integer"), new("enabled", Type: "checkbox")] : [],
        [], PlaceContext.Domain.ValueObjects.JobReturnType.Json, null, 0, 0, [], DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Editor_run_opens_declared_or_json_inferred_fields_before_submission(bool declared)
    {
        var queue = new Queue();
        var vm = new JobChainsViewModel(null!, null!, null!, new Navigation(), submissions: queue);
        var job = InputJob(Guid.NewGuid(), declared);
        var chain = Workflow() with { ProjectId = job.ProjectId, Stages = [new([new(job.Id, job.Name)])] };
        vm.Initialize(job.ProjectId);
        await vm.RunChainAsync(chain, [job]);
        Assert.Same(chain, vm.RunPromptChain);
        Assert.Single(vm.RunPromptSteps);
        Assert.Equal(0, queue.Count);
        vm.SetArg("step0:question", "");
        await vm.SubmitRunPromptAsync();
        Assert.NotNull(vm.RunPromptError);
        Assert.Equal(0, queue.Count);
        vm.SetArg("step0:question", "A real question");
        await vm.SubmitRunPromptAsync();
        Assert.Equal(1, queue.Count);
        using var payload = JsonDocument.Parse(queue.Payload!);
        Assert.Equal("A real question", payload.RootElement.GetProperty("question").GetString());
        Assert.Equal(3, payload.RootElement.GetProperty("count").GetInt32());
        Assert.False(payload.RootElement.GetProperty("enabled").GetBoolean());
    }

    [Fact]
    public async Task Missing_inputs_open_a_form_and_submit_generated_identifiers_as_typed_initial_data()
    {
        var queue = new Queue();
        var vm = new JobChainsViewModel(null!, null!, null!, new Navigation(), submissions: queue);
        var chain = Workflow();
        await vm.RunChainAsync(chain);
        Assert.Same(chain, vm.RunPromptChain); Assert.Equal(0, queue.Count);
        var uuid = Assert.Single(vm.RunInputFields); uuid.Name = "request_id"; uuid.Type = "uuid";
        vm.GenerateRunInputDefault(uuid); var generated = uuid.Value;
        vm.GenerateRunInputDefault(uuid); Assert.Equal(generated, uuid.Value);
        vm.AddRunInputField(); var integer = vm.RunInputFields[1]; integer.Name = "record_id"; integer.Type = "integer"; vm.GenerateRunInputDefault(integer);
        vm.RunInputFields.Add(new() { Name = "question", Value = "Help me sign in" });
        vm.RunInputFields.Add(new() { Name = "customer.active", Type = "checkbox", Value = "false" });
        await vm.SubmitRunPromptAsync();
        Assert.Null(vm.RunPromptError); Assert.Equal(1, queue.Count);
        using var data = JsonDocument.Parse(queue.Payload!);
        Assert.Equal(Guid.Parse(generated), data.RootElement.GetProperty("request_id").GetGuid());
        Assert.True(data.RootElement.GetProperty("record_id").GetInt32() > 0);
        Assert.Equal("Help me sign in", data.RootElement.GetProperty("question").GetString());
        Assert.False(data.RootElement.GetProperty("customer").GetProperty("active").GetBoolean());
        Assert.Null(vm.RunPromptChain); Assert.Empty(vm.RunInputFields);
    }

    [Theory]
    [InlineData("", "uuid", "bad")]
    [InlineData("request_id", "uuid", "bad")]
    [InlineData("count", "integer", "2.5")]
    [InlineData("customer..name", "text", "Ada")]
    public async Task Invalid_input_stays_in_the_form_and_does_not_queue(string name, string type, string value)
    {
        var queue = new Queue();
        var vm = new JobChainsViewModel(null!, null!, null!, new Navigation(), submissions: queue);
        await vm.RunChainAsync(Workflow());
        var field = Assert.Single(vm.RunInputFields); field.Name = name; field.Type = type; field.Value = value;
        await vm.SubmitRunPromptAsync();
        Assert.NotNull(vm.RunPromptError); Assert.NotNull(vm.RunPromptChain); Assert.Equal(0, queue.Count);
        Assert.Equal(value, field.Value);
        vm.CancelRunPrompt(); Assert.Empty(vm.RunInputFields); Assert.Null(vm.RunPromptChain);
    }

    private static JobChainView Workflow() => new(Guid.NewGuid(), Guid.NewGuid(), "Review", null,
        [new([], Action: new QueryDataChainActionView("SELECT 1", "data"))], DateTimeOffset.UtcNow);

    private sealed class Navigation : NavigationManager
    {
        public Navigation() => Initialize("http://localhost/", "http://localhost/");
        protected override void NavigateToCore(string uri, NavigationOptions options) => Uri = ToAbsoluteUri(uri).AbsoluteUri;
    }

    private sealed class Queue : IJobChainSubmissionQueue
    {
        public string? Payload;
        public int Count;
        public Task<JobChainSubmission> EnqueueAsync(Guid projectId, Guid chainId, string? inputPayload, string? idempotencyKey = null,
            CancellationToken ct = default, Guid? queueId = null, int? messageTtlSeconds = null, int receiptRetentionDays = 30,
            int maxAttempts = 3, int retryDelaySeconds = 3, DateTimeOffset? availableAt = null,
            IReadOnlyDictionary<int, string>? stepPayloadOverrides = null, Guid? replayOriginalRunId = null, int? replayFromStepIndex = null)
        {
            Payload = inputPayload; Count++;
            return Task.FromResult(new JobChainSubmission(Guid.NewGuid(), projectId, chainId, Guid.NewGuid(), "Queued", 0, null, DateTimeOffset.UtcNow, null, null));
        }
        public Task<string> ResumeAsync(Guid chainRunId, Guid inputRequestId, string valuesJson, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<JobChainSubmission?> GetAsync(Guid trackingId, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
