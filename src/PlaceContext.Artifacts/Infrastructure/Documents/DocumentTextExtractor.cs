using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using PlaceContext.Application.Ports;
using UglyToad.PdfPig;

namespace PlaceContext.Artifacts.Infrastructure.Documents;

/// <summary>
/// Extracts bounded, model-ready text from user documents. PdfPig handles PDF content streams;
/// Open XML documents are read directly from their ZIP/XML representation; text and data files
/// are decoded without third-party parsers. Any malformed or unsupported file returns null.
/// </summary>
public sealed class DocumentTextExtractor : IDocumentTextExtractor
{
    private const int MaxPages = 50;
    private const int MaxChars = 60_000;
    private const long MaxXmlEntryBytes = 12 * 1024 * 1024;
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".csv", ".tsv", ".json", ".txt", ".md", ".xml", ".yaml", ".yml", ".log", ".sql",
    };

    public string? ExtractText(byte[] content, string fileName)
    {
        if (content.Length == 0) return null;
        var extension = Path.GetExtension(fileName);

        try
        {
            var text = extension.ToLowerInvariant() switch
            {
                ".pdf" => ExtractPdf(content),
                ".docx" => ExtractDocx(content),
                ".xlsx" => ExtractXlsx(content),
                _ when TextExtensions.Contains(extension) => DecodeText(content),
                _ => null,
            };

            if (string.IsNullOrWhiteSpace(text)) return null;
            text = text.Replace("\0", "", StringComparison.Ordinal).Trim();
            return text.Length <= MaxChars ? text : text[..MaxChars] + "\n\n[content truncated]";
        }
        catch
        {
            return null;
        }
    }

    private static string ExtractPdf(byte[] content)
    {
        using var document = PdfDocument.Open(content);
        var builder = new StringBuilder();
        foreach (var page in document.GetPages())
        {
            if (page.Number > MaxPages || builder.Length >= MaxChars) break;
            builder.Append(page.Text).Append('\n');
        }
        return builder.ToString();
    }

    private static string ExtractDocx(byte[] content)
    {
        using var archive = OpenArchive(content);
        var entry = archive.GetEntry("word/document.xml")
            ?? throw new InvalidDataException("DOCX document.xml is missing.");
        var document = LoadXml(entry);
        XNamespace word = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        var builder = new StringBuilder();

        foreach (var paragraph in document.Descendants(word + "p"))
        {
            foreach (var node in paragraph.Descendants())
            {
                if (node.Name == word + "t")
                    builder.Append(node.Value);
                else if (node.Name == word + "tab")
                    builder.Append('\t');
                else if (node.Name == word + "br")
                    builder.AppendLine();
            }
            builder.AppendLine();
            if (builder.Length >= MaxChars) break;
        }

        return builder.ToString();
    }

    private static string ExtractXlsx(byte[] content)
    {
        using var archive = OpenArchive(content);
        XNamespace spreadsheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var sharedStrings = ReadSharedStrings(archive, spreadsheet);
        var builder = new StringBuilder();

        foreach (var entry in archive.Entries
                     .Where(entry => entry.FullName.StartsWith("xl/worksheets/sheet", StringComparison.Ordinal) &&
                                     entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(entry => entry.FullName, StringComparer.Ordinal))
        {
            if (builder.Length > 0) builder.AppendLine();
            builder.AppendLine($"## {Path.GetFileNameWithoutExtension(entry.Name)}");
            var document = LoadXml(entry);

            foreach (var row in document.Descendants(spreadsheet + "row"))
            {
                var cells = new List<string>();
                foreach (var cell in row.Elements(spreadsheet + "c"))
                {
                    var type = (string?)cell.Attribute("t");
                    var raw = cell.Element(spreadsheet + "v")?.Value ??
                              cell.Descendants(spreadsheet + "t").FirstOrDefault()?.Value ?? "";
                    if (type == "s" && int.TryParse(raw, out var sharedIndex) &&
                        sharedIndex >= 0 && sharedIndex < sharedStrings.Count)
                        raw = sharedStrings[sharedIndex];
                    cells.Add(raw.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' '));
                }
                builder.AppendLine(string.Join('\t', cells));
                if (builder.Length >= MaxChars) break;
            }

            if (builder.Length >= MaxChars) break;
        }

        return builder.ToString();
    }

    private static IReadOnlyList<string> ReadSharedStrings(ZipArchive archive, XNamespace spreadsheet)
    {
        var entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry is null) return Array.Empty<string>();
        var document = LoadXml(entry);
        return document.Descendants(spreadsheet + "si")
            .Select(item => string.Concat(item.Descendants(spreadsheet + "t").Select(text => text.Value)))
            .ToList();
    }

    private static string DecodeText(byte[] content)
    {
        using var stream = new MemoryStream(content, writable: false);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var text = reader.ReadToEnd();
        var controlCount = text.Count(character =>
            char.IsControl(character) && character is not '\r' and not '\n' and not '\t');
        return controlCount > Math.Max(4, text.Length / 100) ? "" : text;
    }

    private static ZipArchive OpenArchive(byte[] content) =>
        new(new MemoryStream(content, writable: false), ZipArchiveMode.Read);

    private static XDocument LoadXml(ZipArchiveEntry entry)
    {
        if (entry.Length > MaxXmlEntryBytes)
            throw new InvalidDataException("Document XML is too large.");
        using var stream = entry.Open();
        return XDocument.Load(stream, LoadOptions.None);
    }
}
