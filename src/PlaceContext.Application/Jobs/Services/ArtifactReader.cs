using PlaceContext.Application.Artifacts;
using PlaceContext.Application.Features;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Jobs;

public sealed class ArtifactReader(IRunArtifactLinkRepository links, IObjectStore store)
{
    public async Task<ArtifactFileView?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var link = await links.GetByIdAsync(id, ct);
        return link is null ? null : new(link.Id, link.RunId, link.JobId, link.ProjectId, link.Kind.ToString(),
            link.Title, link.ContentType, link.SizeBytes, link.CreatedAt);
    }

    public async Task<ArtifactContent?> OpenAsync(Guid id, Guid? expectedRunId = null, CancellationToken ct = default)
    {
        var link = await links.GetByIdAsync(id, ct);
        if (link is null || expectedRunId is { } run && link.RunId != run)
        {
            return null;
        }
        var content = await store.OpenReadAsync(link.Bucket, link.ObjectKey, ct);
        if (content is null)
        {
            return null;
        }
        var contentType = string.IsNullOrWhiteSpace(content.ContentType)
                    || content.ContentType.Equals("application/octet-stream", StringComparison.OrdinalIgnoreCase)
                    ? link.ContentType : content.ContentType;
        if (string.IsNullOrWhiteSpace(contentType))
        {
            contentType = "application/octet-stream";
        }
        var filename = link.ObjectKey[(link.ObjectKey.LastIndexOf('/') + 1)..];
        return new(content.Content, contentType, string.IsNullOrEmpty(filename) ? "artifact" : filename);
    }
}
