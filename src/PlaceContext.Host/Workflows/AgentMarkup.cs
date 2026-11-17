using System.Text.Json;
using System.Text.Json.Serialization;
using PlaceContext.Application.Build;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace PlaceContext.Host.Workflows;

public static class AgentMarkup
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 8
    };

    public static string Write(DashboardAgentProfile agent)
    {
        var configuration = JsonSerializer.SerializeToNode(agent, Json)!;
        configuration["parentAgentId"] = JsonSerializer.SerializeToNode(agent.EffectiveParentAgentId, Json);
        var graph = new DeserializerBuilder().WithAttemptingUnquotedStringTypeDeserialization().Build()
            .Deserialize<object>(configuration.ToJsonString());
        return new SerializerBuilder().WithQuotingNecessaryStrings().DisableAliases().Build().Serialize(graph);
    }

    public static DashboardAgentProfile Read(string yaml, Guid agentId)
    {
        if (string.IsNullOrWhiteSpace(yaml) || yaml.Length > 65536)
            throw new ArgumentException("Enter an agent YAML configuration of up to 64 KB.");
        var parser = new Parser(new StringReader(yaml));
        var depth = 0; var events = 0; var documents = 0;
        while (parser.MoveNext())
        {
            if (++events > 4000 || parser.Current is AnchorAlias)
                throw new ArgumentException("Agent YAML must not contain aliases or more than 4,000 values.");
            if (parser.Current is NodeEvent node && !node.Tag.IsEmpty)
                throw new ArgumentException("Explicit YAML tags are not supported.");
            if (parser.Current is DocumentStart && ++documents > 1)
                throw new ArgumentException("Apply one agent configuration at a time.");
            if (parser.Current is MappingStart or SequenceStart && ++depth > 8)
                throw new ArgumentException("Agent YAML is nested too deeply.");
            if (parser.Current is MappingEnd or SequenceEnd) depth--;
        }
        var graph = new DeserializerBuilder().WithAttemptingUnquotedStringTypeDeserialization().WithDuplicateKeyChecking()
            .Build().Deserialize<object>(yaml);
        var json = new SerializerBuilder().JsonCompatible().WithMaximumRecursion(8).Build().Serialize(graph);
        var agent = JsonSerializer.Deserialize<DashboardAgentProfile>(json, Json)
            ?? throw new ArgumentException("An agent configuration is required.");
        if (agent.Id != agentId) throw new ArgumentException("The id must match the agent open in this workspace.");
        if (agent.Id != DashboardSettings.DefaultAgentId && agent.ParentAgentId == DashboardSettings.DefaultAgentId)
            agent.ParentAgentId = null;
        agent.Validate();
        return agent;
    }
}
