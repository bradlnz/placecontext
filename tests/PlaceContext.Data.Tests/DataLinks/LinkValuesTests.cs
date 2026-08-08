using PlaceContext.Application.Features;
using Xunit;

namespace PlaceContext.Application.Tests.DataLinks;

/// <summary>The pure normalization/classification helpers behind the record-link index.</summary>
public class LinkValuesTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData("  233   Gympie    Rd ", "233 gympie rd")]
    [InlineData("Hello\tWorld\n Foo", "hello world foo")]
    [InlineData("AB\u200BCD\uFEFF", "abcd")] // zero-width chars stripped
    [InlineData("+61 (0) 400-123 456", "+610400123456")] // phone-ish → digits + leading +
    [InlineData("555-1234", "5551234")]
    [InlineData("2026-07-18", "2026-07-18")] // an ISO date is never phone-reduced
    [InlineData("User@Example.COM", "user@example.com")]
    [InlineData("123 Main St", "123 main st")] // letters → not phone-shaped
    public void Normalize_folds_case_whitespace_zero_width_and_phones(string? input, string expected)
        => Assert.Equal(expected, LinkValues.Normalize(input));

    [Theory]
    // Value shape wins over the column name.
    [InlineData("notes", "jane@example.com", "email")]
    [InlineData("notes", "+61 400 123 456", "phone")]
    [InlineData("notes", "2026-07-18", "text")] // ISO date is not a phone
    [InlineData("notes", "just some text", "text")]
    // Column-name allowlists.
    [InlineData("address", "anything", "address")]
    [InlineData("suburb", "x", "address")]
    [InlineData("postcode", "4000", "address")]
    [InlineData("country", "x", "address")]
    [InlineData("email", "x", "email")]
    [InlineData("contact_email", "x", "email")]
    [InlineData("mobile", "x", "phone")]
    [InlineData("fax_number", "x", "phone")]
    [InlineData("homePhone", "x", "phone")] // camelCase tokenizes
    [InlineData("contact_name", "x", "name")]
    [InlineData("company", "x", "name")]
    [InlineData("website", "x", "url")]
    [InlineData("domain", "x", "url")]
    [InlineData("price", "x", "text")]
    public void Classify_prefers_value_shape_then_column_allowlists(string column, string value, string expected)
        => Assert.Equal(expected, LinkValues.Classify(column, value));

    [Theory]
    [InlineData("street", true)]
    [InlineData("city", true)]
    [InlineData("state", true)]
    [InlineData("email", true)]
    [InlineData("phone", true)]
    [InlineData("name", true)]
    [InlineData("title", true)]
    [InlineData("company", true)]
    [InlineData("url", true)]
    [InlineData("EmailAddress", true)]
    [InlineData("notes", false)]
    [InlineData("price", false)]
    [InlineData("id", false)]
    [InlineData("created_at", false)]
    [InlineData("status", false)] // whole-token matching: "state" does not leak into "status"
    public void IsIdentityColumn_matches_the_allowlists_by_token(string column, bool expected)
        => Assert.Equal(expected, LinkValues.IsIdentityColumn(column));

    [Theory]
    [InlineData("text", "abc", false)] // too short
    [InlineData("text", "ab cd", true)]
    [InlineData("email", "jane@example.com", true)]
    [InlineData("phone", "0400123456", true)] // pure digits are fine for phones
    [InlineData("text", "0400123456", false)] // … but not for anything else
    [InlineData("text", "3fa85f64-5717-4562-b3fc-2c963f66afa6", false)] // GUID
    [InlineData("text", "2026-07-18", false)] // ISO date
    [InlineData("text", "2026-07-18T23:14:06Z", false)]
    [InlineData("text", "true", false)]
    [InlineData("text", "false", false)]
    [InlineData("address", "233 gympie rd", true)]
    public void IsLinkable_rejects_values_not_worth_linking(string kind, string value, bool expected)
        => Assert.Equal(expected, LinkValues.IsLinkable(kind, value));

    [Fact]
    public void IsLinkable_rejects_overlong_values()
        => Assert.False(LinkValues.IsLinkable("text", new string('x', 301)));
}
