namespace PlaceContext.Application.Ports;

/// <summary>
/// Extracts searchable text from a document artifact (PDF today). Used by the entity tagger so a
/// report that mentions a known key — a site's address in a PDF — links its run into the data
/// graph. Best-effort: null means "no text recoverable", never an exception.
/// </summary>
public interface IDocumentTextExtractor
{
    string? ExtractText(byte[] content, string fileName);
}
