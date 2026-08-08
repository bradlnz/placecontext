using PlaceContext.Application.Dtos;
using PlaceContext.Domain.Entities;

namespace PlaceContext.Application.Features;

/// <summary>Translates trigger domain entities to read model views.</summary>
internal static class TriggerViewMapper
{
    public static TriggerView ToView(JobTrigger t) => new(
        Id: t.Id,
        ProjectId: t.ProjectId,
        JobId: t.JobId,
        Name: t.Name,
        Kind: t.Kind.ToString(),
        Enabled: t.Enabled,
        CronExpression: t.CronExpression,
        EventName: t.EventName,
        ChainId: t.ChainId,
        SourceTable: t.SourceTable,
        Prompt: t.Prompt,
        CommandId: t.CommandId,
        NextRunAt: t.NextRunAt,
        LastFiredAt: t.LastFiredAt,
        CreatedAt: t.CreatedAt);
}
