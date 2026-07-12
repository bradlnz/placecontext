using PlaceContext.Application.Ports;
using UglyToad.PdfPig;

namespace PlaceContext.Infrastructure.Documents;

/// <summary>
/// <see cref="IDocumentTextExtractor"/> backed by PdfPig (Apache-2.0, pure managed): real content
/// decoding — compressed streams, font subsetting, encoding maps — where a byte-level sniff only
/// ever saw glyph noise. Any parse failure returns null; the tagger treats that as "no text".
/// </summary>
public sealed class PdfPigTextExtractor : IDocumentTextExtractor
{
    private const int MaxPages = 50;
    private const int MaxChars = 500_000;

    public string? ExtractText(byte[] content, string fileName)
    {
        if (!fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) return null;
        try
        {
            using var document = PdfDocument.Open(content);
            var sb = new System.Text.StringBuilder();
            foreach (var page in document.GetPages())
            {
                if (page.Number > MaxPages || sb.Length > MaxChars) break;
                sb.Append(page.Text).Append('\n');
            }
            return sb.ToString();
        }
        catch
        {
            return null;
        }
    }
}
