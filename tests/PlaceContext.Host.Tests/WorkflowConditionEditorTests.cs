using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PlaceContext.Application;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.Host.Components.Shared;
using PlaceContext.Host.Components.ViewModels;
using static PlaceContext.Host.Components.ViewModels.JobChainsViewModel;

namespace PlaceContext.Host.Tests;

public sealed class WorkflowConditionEditorTests
{
    [Theory]
    [InlineData("eq:approved:true", "boolean", "{\"approved\":true}")]
    [InlineData("eq:approved:false", "boolean", "{\"approved\":false}")]
    [InlineData("gte:score:0.9", "number", "{\"score\":0.95}")]
    [InlineData("eq:missing:null", "null", "{\"missing\":null}")]
    [InlineData("eq:status:ready", "text", "{\"status\":\"ready\"}")]
    public void Typed_values_round_trip_and_evaluate(string expression, string type, string payload)
    {
        var row = ConditionRow.Parse(expression);
        Assert.Equal(type, row.InputType);
        Assert.Equal(expression, row.Expression);
        Assert.True(new ConditionGate(row.Expression).Evaluate(payload).Proceed);
        row.SetValueType("boolean");
        Assert.Contains(row.Value, new[] { "true", "false" });
        row.SetValueType("null");
        Assert.Equal("null", row.Value);
    }

    [Fact]
    public async Task Suggestions_include_typed_previous_outputs_and_actions_but_not_future_fields()
    {
        var store = new Store();
        var vm = new JobChainsViewModel(new PlaceContextService(store, null!, null!, null!), null!, null!, null!);
        vm.EdStages.Add([store.JobId]); vm.EdStages.Add([]); vm.EdStages.Add([]);
        vm.EdStageGates[0] = new ConditionGate(ConditionGate.AutomaticExpression, ReviewQuestion: "Ready?");
        vm.EdStageActions[1] = new LoopChainAction(LoopChainAction.HumanType, "Approve?", "approval", LoopChainAction.YesNoResponse);
        vm.EdStageActions[2] = new QueryDataChainAction("SELECT 1", "future");
        var fields = await vm.LoadConditionFieldsAsync(2);
        Assert.Null(fields.Notice);
        Assert.Contains(new ConditionField("ai_gate_review.noul", "number"), fields.Fields);
        Assert.Contains(new ConditionField("data.ready", "boolean"), fields.Fields);
        Assert.Contains(new ConditionField("data.count", "number"), fields.Fields);
        Assert.Contains(new ConditionField("data.name", "text"), fields.Fields);
        Assert.Contains(new ConditionField("data.items", "array"), fields.Fields);
        Assert.Contains(new ConditionField("approval", "boolean"), fields.Fields);
        Assert.DoesNotContain(fields.Fields, field => field.Path.StartsWith("future") || field.Path.Contains("items.0") || field.Path.Contains("bad.key"));
        store.Fail = true;
        fields = await vm.LoadConditionFieldsAsync(2);
        Assert.NotNull(fields.Notice);
        Assert.Contains(new ConditionField("approval", "boolean"), fields.Fields);
    }

    [Theory]
    [InlineData("all", "eq:approved:true", "<select aria-label=\"Condition value\"", "All conditions true?")]
    [InlineData("any", "gte:score:0.9", "type=\"number\"", "Any condition true?")]
    public async Task Shared_editor_renders_canvas_routes_tags_and_typed_controls(string match, string expression, string input, string decision)
    {
        await using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var result = await renderer.RenderComponentAsync<WorkflowConditionsEditor>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                ["Rows"] = new List<ConditionRow> { ConditionRow.Parse(expression), ConditionRow.Parse("exists:data") },
                ["Match"] = match, ["TrueLabel"] = "Deliver report", ["FalseLabel"] = "Request review",
                ["LoadFields"] = (Func<Task<ConditionFieldOptions>>)(() => Task.FromResult(new ConditionFieldOptions([new("approved", "boolean"), new("score", "number")])))
            }));
            return result.ToHtmlString();
        });
        Assert.Contains("Condition logic canvas", html);
        Assert.Contains(decision, html);
        Assert.Contains("Deliver report", html); Assert.Contains("Request review", html);
        Assert.Contains("Continue to next stage", html);
        Assert.Contains("Edit condition 2", html);
        Assert.Contains("<datalist", html); Assert.Contains("field-tag", html);
        Assert.Contains(input, html);
    }

    private sealed class Store : IDispatcher
    {
        public Guid JobId = Guid.NewGuid();
        public Guid RunId = Guid.NewGuid();
        public bool Fail;
        public Task<T> Send<T>(ICommand<T> command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<T> Query<T>(IQuery<T> query, CancellationToken ct = default)
        {
            if (Fail) throw new InvalidOperationException("Unavailable");
            object result = query switch
            {
                ListJobRunsQuery => new[] { new JobRunView(RunId, JobId, "Succeeded", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 1, 1, 0, 0) },
                GetJobRunQuery => new JobRunDetailView(RunId, JobId, Guid.NewGuid(), "Succeeded", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
                    [new(0, 0, "Succeeded", """{"data":{"ready":true,"count":3,"name":"report","items":[1],"bad.key":false}}""", null, [])], null, null!),
                _ => throw new NotSupportedException()
            };
            return Task.FromResult((T)result);
        }
    }
}
