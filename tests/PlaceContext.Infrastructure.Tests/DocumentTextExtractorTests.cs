using System.IO.Compression;
using System.Text;
using PlaceContext.Infrastructure.Documents;
using Xunit;

namespace PlaceContext.Infrastructure.Tests;

public class DocumentTextExtractorTests
{
    [Fact]
    public void Extracts_text_from_a_real_reportlab_pdf()
    {
        // The fixture is a genuine reportlab multi-page report (compressed streams, subset
        // fonts) — exactly what the byte-level sniffer could NOT read. The extractor must
        // recover the addresses so entity tagging works for PDF return types.
        var pdf = File.ReadAllBytes(Path.Combine("TestData", "property-report.pdf"));
        var text = new DocumentTextExtractor().ExtractText(pdf, "property-report.pdf");
        Assert.NotNull(text);
        Assert.Contains("1 Test St", text);
        Assert.Contains("Riverbend Terrace", text);
    }

    [Fact]
    public void Extracts_csv_and_json_as_text()
    {
        var extractor = new DocumentTextExtractor();

        var csv = extractor.ExtractText(Encoding.UTF8.GetBytes("name,total\nAlpha,42"), "report.csv");
        var json = extractor.ExtractText(Encoding.UTF8.GetBytes("{\"status\":\"ready\"}"), "report.json");

        Assert.Contains("Alpha,42", csv);
        Assert.Contains("\"status\":\"ready\"", json);
    }

    [Fact]
    public void Extracts_paragraphs_from_docx()
    {
        var docx = Archive(
            ("word/document.xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:body>
                    <w:p><w:r><w:t>Quarterly report</w:t></w:r></w:p>
                    <w:p><w:r><w:t>Revenue increased by 12%.</w:t></w:r></w:p>
                  </w:body>
                </w:document>
                """));

        var text = new DocumentTextExtractor().ExtractText(docx, "report.docx");

        Assert.Contains("Quarterly report", text);
        Assert.Contains("Revenue increased by 12%.", text);
    }

    [Fact]
    public void Extracts_rows_and_shared_strings_from_xlsx()
    {
        var xlsx = Archive(
            ("xl/sharedStrings.xml",
                """
                <sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                  <si><t>Suburb</t></si><si><t>New Farm</t></si>
                </sst>
                """),
            ("xl/worksheets/sheet1.xml",
                """
                <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                  <sheetData>
                    <row><c t="s"><v>0</v></c><c><v>Count</v></c></row>
                    <row><c t="s"><v>1</v></c><c><v>7</v></c></row>
                  </sheetData>
                </worksheet>
                """));

        var text = new DocumentTextExtractor().ExtractText(xlsx, "sites.xlsx");

        Assert.Contains("Suburb\tCount", text);
        Assert.Contains("New Farm\t7", text);
    }

    [Fact]
    public void Unsupported_and_malformed_files_return_null()
    {
        var extractor = new DocumentTextExtractor();
        Assert.Null(extractor.ExtractText([1, 2, 3], "archive.zip"));
        Assert.Null(extractor.ExtractText([1, 2, 3], "broken.pdf"));
        Assert.Null(extractor.ExtractText([1, 2, 3], "broken.docx"));
    }

    private static byte[] Archive(params (string Path, string Content)[] files)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var file in files)
            {
                var entry = archive.CreateEntry(file.Path);
                using var stream = entry.Open();
                using var writer = new StreamWriter(stream, new UTF8Encoding(false));
                writer.Write(file.Content);
            }
        }
        return output.ToArray();
    }
}
