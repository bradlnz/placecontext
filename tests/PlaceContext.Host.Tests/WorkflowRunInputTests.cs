using System.Text.Json;
using Microsoft.AspNetCore.Components;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Jobs;
using PlaceContext.Host.Components.ViewModels;

namespace PlaceContext.Host.Tests;

public sealed class WorkflowRunInputTests
{
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
