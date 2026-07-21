using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class GetAgentConfigHandler : IQueryHandler<GetAgentConfigQuery, AgentConfigView>
{
    private readonly IAgentConfigRepository _configs;

    public GetAgentConfigHandler(IAgentConfigRepository configs) => _configs = configs;

    public async Task<AgentConfigView> HandleAsync(GetAgentConfigQuery query, CancellationToken ct = default)
    {
        var config = await _configs.GetByProjectIdAsync(query.ProjectId, ct);
        if (config is null)
            return AgentConfigViewMapper.Default(query.ProjectId);
        return AgentConfigViewMapper.ToView(config);
    }
}

internal static class AgentConfigViewMapper
{
    public static AgentConfigView ToView(AgentConfig c) => new(
        c.Id, c.ProjectId, c.BaseModel, c.SystemPrompt,
        c.MaxContextChunks, c.Temperature, c.TopP, c.Enabled,
        c.CreatedAt, c.UpdatedAt);

    public static AgentConfigView Default(Guid projectId) => new(
        Guid.Empty, projectId,
        AgentConfig.DefaultBaseModel,
        "You are a helpful assistant for this project. Use the provided context to answer questions accurately.",
        AgentConfig.DefaultMaxContextChunks, AgentConfig.DefaultTemperature, AgentConfig.DefaultTopP,
        false, DateTimeOffset.MinValue, DateTimeOffset.MinValue);
}
