using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class UpdateAgentConfigHandler : ICommandHandler<UpdateAgentConfigCommand, AgentConfigView>
{
    private readonly IAgentConfigRepository _configs;
    private readonly IAgentChatUnitOfWork _uow;
    private readonly IClock _clock;

    public UpdateAgentConfigHandler(IAgentConfigRepository configs, IAgentChatUnitOfWork uow, IClock clock)
    {
        _configs = configs;
        _uow = uow;
        _clock = clock;
    }

    public async Task<AgentConfigView> HandleAsync(UpdateAgentConfigCommand command, CancellationToken ct = default)
    {
        var config = await _configs.GetByProjectIdAsync(command.ProjectId, ct);
        if (config is null)
        {
            config = AgentConfig.Create(command.ProjectId, _clock.UtcNow);
            config.Update(command.BaseModel, command.SystemPrompt, command.Preamble, command.ToolCatalog,
                command.LaunchpadToolCatalog, command.MaxContextChunks,
                command.Temperature, command.TopP, command.Enabled, _clock.UtcNow);
            await _configs.AddAsync(config, ct);
        }
        else
        {
            config.Update(command.BaseModel, command.SystemPrompt, command.Preamble, command.ToolCatalog,
                command.LaunchpadToolCatalog, command.MaxContextChunks,
                command.Temperature, command.TopP, command.Enabled, _clock.UtcNow);
            await _configs.UpdateAsync(config, ct);
        }

        await _uow.SaveChangesAsync(ct);
        return AgentConfigViewMapper.ToView(config);
    }
}
