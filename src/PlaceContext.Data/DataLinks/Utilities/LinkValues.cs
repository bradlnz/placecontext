using System.Globalization;
using System.Text.RegularExpressions;

namespace PlaceContext.Application.Features;

/// <summary>
/// Pure helpers for the record-link index: how identity-ish values are normalized for comparison,
/// how a column/value pair is classified into a link kind, and which values are worth linking at all.
/// </summary>
public static class LinkValues
{
    public const string EmailKind = "email";
    public const string PhoneKind = "phone";
    public const string AddressKind = "address";
    public const string NameKind = "name";
    public const string UrlKind = "url";
    public const string TextKind = "text";

    // Column-name allowlists, checked in this order (first match wins).
    private static readonly HashSet<string> AddressTokens = new(StringComparer.Ordinal)
        { "address", "location", "street", "suburb", "city", "postcode", "state", "country" };
    private static readonly HashSet<string> EmailTokens = new(StringComparer.Ordinal) { "email" };
    private static readonly HashSet<string> PhoneTokens = new(StringComparer.Ordinal) { "phone", "mobile", "fax" };
    private static readonly HashSet<string> NameTokens = new(StringComparer.Ordinal) { "name", "title", "contact", "company" };
    private static readonly HashSet<string> UrlTokens = new(StringComparer.Ordinal) { "url", "website", "domain" };

    private static readonly Regex ZeroWidth = new("[\u200B-\u200D\uFEFF]", RegexOptions.Compiled);
    private static readonly Regex WhitespaceRun = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex EmailShape = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled);
    private static readonly Regex IsoDateShape = new(
        @"^\d{4}-\d{2}-\d{2}([T ]\d{2}:\d{2}(:\d{2}(\.\d+)?)?\s*(Z|[+-]\d{2}:?\d{2})?)?$", RegexOptions.Compiled);
    private static readonly Regex CamelBoundary = new(@"(?<=[a-z0-9])(?=[A-Z])", RegexOptions.Compiled);
    private static readonly Regex TokenSplit = new(@"[^a-zA-Z0-9]+", RegexOptions.Compiled);

    /// <summary>
    /// The comparison form of a value: trimmed, whitespace runs collapsed to one space, zero-width
    /// characters stripped, case-folded. Phone-ish values reduce to digits plus a leading '+' (but
    /// ISO dates never do — "2026-07-18" is not a phone number). Empty in, empty out.
    /// </summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var collapsed = WhitespaceRun.Replace(ZeroWidth.Replace(value, ""), " ").Trim().ToLowerInvariant();
        if (collapsed.Length == 0) return "";
        return IsPhoneShaped(collapsed, minDigits: 5) && !IsoDateShape.IsMatch(collapsed)
            ? PhoneDigits(collapsed)
            : collapsed;
    }

    /// <summary>The link kind of a column/value pair: the value's shape wins (email, then phone);
    /// otherwise the column name's allowlist (address → email → phone → name → url); else text.</summary>
    public static string Classify(string columnName, string value)
    {
        if (EmailShape.IsMatch(value.Trim())) return EmailKind;
        if (IsPhoneShaped(value.Trim(), minDigits: 7) && !IsoDateShape.IsMatch(value.Trim())) return PhoneKind;
        var tokens = Tokens(columnName);
        if (tokens.Any(AddressTokens.Contains)) return AddressKind;
        if (tokens.Any(EmailTokens.Contains)) return EmailKind;
        if (tokens.Any(PhoneTokens.Contains)) return PhoneKind;
        if (tokens.Any(NameTokens.Contains)) return NameKind;
        if (tokens.Any(UrlTokens.Contains)) return UrlKind;
        return TextKind;
    }

    /// <summary>True when the column name hits any identity allowlist (address/email/phone/name/url).</summary>
    public static bool IsIdentityColumn(string columnName)
        => Classify(columnName, "") is not TextKind;

    /// <summary>
    /// The bar a normalized value must clear to be worth linking: long enough to be meaningful,
    /// short enough to be a value, and not a number (phones excepted), GUID, ISO date, or boolean.
    /// </summary>
    public static bool IsLinkable(string kind, string value)
    {
        if (value.Length < 4 || value.Length > 300) return false;
        if (value is "true" or "false") return false;
        if (kind != PhoneKind && value.All(char.IsDigit)) return false;
        if (Guid.TryParse(value, out _)) return false;
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) return false;
        return true;
    }

    // A column name splits into lowercase tokens on non-alphanumeric runs and camelCase boundaries,
    // so "email_address", "email-address" and "EmailAddress" all tokenize to (email, address).
    private static string[] Tokens(string columnName)
        => TokenSplit.Split(CamelBoundary.Replace(columnName, " ").ToLowerInvariant())
            .Where(t => t.Length > 0).ToArray();

    private static bool IsPhoneShaped(string value, int minDigits)
        => value.Count(char.IsDigit) >= minDigits
            && value.All(c => char.IsDigit(c) || c is ' ' or '+' or '(' or ')' or '-' or '.');

    private static string PhoneDigits(string value)
    {
        var digits = new string(value.Where(char.IsDigit).ToArray());
        return value.StartsWith('+') ? "+" + digits : digits;
    }
}
