using System.Text.Json;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

internal static class CrmClientArtifactMapper
{
    public static CrmClientArtifactView ToView(CrmClientArtifact value) => new(
        value.Id,
        value.ClientId,
        value.Title,
        value.ContentType,
        value.SizeBytes,
        value.IsDirectUpload ? "Upload" : "Automation",
        value.ChainRunId,
        value.CreatedAt);
}
