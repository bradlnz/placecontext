using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using PlaceContext.Application.Build;
using PlaceContext.Application.Dtos;
using PlaceContext.Domain.ValueObjects;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace PlaceContext.Host.Workflows;

public sealed record WorkflowMarkupDocument(int Version, Guid WorkflowId, string Name, WorkflowStage[] Stages, string? Description = null);

public static class WorkflowMarkup
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        MaxDepth = 32,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { type =>
        {
            if (!typeof(ChainAction).IsAssignableFrom(type.Type)) return;
            foreach (var property in type.Properties.ToArray())
                if (property.Name == "displayName" || property.Name == "type" && type.Type != typeof(LoopChainAction))
                    type.Properties.Remove(property);
        } } }
    };

    public static string Write(JobChainView workflow)
    {
        var document = new WorkflowMarkupDocument(1, workflow.Id, workflow.Name,
            workflow.Stages.Select(WorkflowStage.FromView).ToArray(), workflow.Description);
        var graph = new DeserializerBuilder().WithAttemptingUnquotedStringTypeDeserialization().Build()
            .Deserialize<object>(JsonSerializer.Serialize(document, Json));
        return new SerializerBuilder().WithQuotingNecessaryStrings().DisableAliases().WithMaximumRecursion(32).Build().Serialize(graph);
    }

    public static WorkflowMarkupDocument Read(string yaml, Guid workflowId)
    {
        if (string.IsNullOrWhiteSpace(yaml) || yaml.Length > 262144)
            throw new ArgumentException("Enter a workflow YAML document of up to 256 KB.");
        var parser = new Parser(new StringReader(yaml));
        var depth = 0; var events = 0; var documents = 0;
        while (parser.MoveNext())
        {
            if (++events > 20000 || parser.Current is AnchorAlias)
                throw new ArgumentException("Workflow YAML must not contain aliases or more than 20,000 values.");
            if (parser.Current is NodeEvent node && !node.Tag.IsEmpty)
                throw new ArgumentException("Explicit YAML tags are not supported in workflow definitions.");
            if (parser.Current is DocumentStart && ++documents > 1)
                throw new ArgumentException("Deploy one workflow document at a time.");
            if (parser.Current is MappingStart or SequenceStart && ++depth > 32)
                throw new ArgumentException("Workflow YAML is nested too deeply.");
            if (parser.Current is MappingEnd or SequenceEnd) depth--;
        }
        var graph = new DeserializerBuilder().WithAttemptingUnquotedStringTypeDeserialization().WithDuplicateKeyChecking()
            .Build().Deserialize<object>(yaml);
        var json = new SerializerBuilder().JsonCompatible().WithMaximumRecursion(32).Build().Serialize(graph);
        var document = JsonSerializer.Deserialize<WorkflowMarkupDocument>(json, Json)
            ?? throw new ArgumentException("A workflow definition is required.");
        if (document.Version != 1) throw new ArgumentException("Unsupported workflow version. Use version: 1.");
        if (document.WorkflowId != workflowId) throw new ArgumentException("The workflowId must match the workflow open in this editor.");
        if (string.IsNullOrWhiteSpace(document.Name)) throw new ArgumentException("A workflow name is required.");
        if (document.Stages is null || document.Stages.Length is < 1 or > 100 || document.Stages.Any(stage => stage is null))
            throw new ArgumentException("Use 1–100 workflow stages.");
        foreach (var stage in document.Stages) _ = stage.ToStage();
        return document;
    }
}
