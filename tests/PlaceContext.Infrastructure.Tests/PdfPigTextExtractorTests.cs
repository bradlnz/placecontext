using PlaceContext.Infrastructure.Documents;
using Xunit;

namespace PlaceContext.Infrastructure.Tests;

public class PdfPigTextExtractorTests
{
    [Fact]
    public void Extracts_text_from_a_real_reportlab_pdf()
    {
        // The fixture is a genuine reportlab multi-page report (compressed streams, subset
        // fonts) — exactly what the byte-level sniffer could NOT read. The extractor must
        // recover the addresses so entity tagging works for PDF return types.
        var pdf = File.ReadAllBytes(Path.Combine("TestData", "property-report.pdf"));
        var text = new PdfPigTextExtractor().ExtractText(pdf, "property-report.pdf");
        Assert.NotNull(text);
        Assert.Contains("1 Test St", text);
        Assert.Contains("Riverbend Terrace", text);
    }

    [Fact]
    public void Non_pdf_and_garbage_return_null()
    {
        var x = new PdfPigTextExtractor();
        Assert.Null(x.ExtractText(new byte[] { 1, 2, 3 }, "notes.txt"));
        Assert.Null(x.ExtractText(new byte[] { 1, 2, 3 }, "broken.pdf"));
    }
}
