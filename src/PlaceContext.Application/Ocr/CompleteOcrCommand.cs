using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;

namespace PlaceContext.Application.Features;

/// <summary>
/// Records a finished OCR attempt for one artifact. On success (<c>Markdown</c> set, <c>Error</c>
/// null) the extracted text is stored in the project's <c>ocr_results</c> table; on failure only the
/// tracking columns are updated so the artifact leaves the pending set either way. Requires
/// <see cref="Permission.DataWrite"/> (writing into the project database).
/// </summary>
public sealed record CompleteOcrCommand(Guid ArtifactId, string? Markdown, string? Error)
    : ICommand<bool>, IRequiresPermission
{
    string IRequiresPermission.RequiredPermission => Permission.DataWrite;
}
