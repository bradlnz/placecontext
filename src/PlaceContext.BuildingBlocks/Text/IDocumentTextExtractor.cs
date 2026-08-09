namespace PlaceContext.Application.Ports;

/// <summary>
/// Extracts searchable text from supported document artifacts. Used by entity tagging and chat
/// attachments so linked files contribute their content to downstream processing. Best-effort:
/// null means "no text recoverable", never an exception.
/// </summary>
public interface IDocumentTextExtractor
{
    string? ExtractText(byte[] content, string fileName);
}
