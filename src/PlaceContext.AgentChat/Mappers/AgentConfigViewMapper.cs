using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

internal static class AgentConfigViewMapper
{
    public static AgentConfigView ToView(AgentConfig c) => new(
        c.Id, c.ProjectId, c.BaseModel, c.SystemPrompt,
        c.Preamble, c.ToolCatalog, c.LaunchpadToolCatalog,
        c.MaxContextChunks, c.Temperature, c.TopP, c.Enabled,
        c.CreatedAt, c.UpdatedAt);

    public static AgentConfigView Default(Guid projectId) => new(
        Guid.Empty, projectId,
        AgentConfig.DefaultBaseModel,
        "You are a helpful assistant for this project. Use the provided context to answer questions accurately.",
        AgentConfig.DefaultPreamble,
        AgentConfig.DefaultToolCatalog,
        AgentConfig.DefaultLaunchpadToolCatalog,
        AgentConfig.DefaultMaxContextChunks, AgentConfig.DefaultTemperature, AgentConfig.DefaultTopP,
        false, DateTimeOffset.MinValue, DateTimeOffset.MinValue);
}
