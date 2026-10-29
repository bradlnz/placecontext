namespace PlaceContext.Application.Ports;

/// <summary>Strict UTF-8 text detection shared by the artifact capture paths.</summary>
public static class Utf8Text
{
    private static readonly System.Text.UTF8Encoding Strict = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>
    /// Decodes <paramref name="bytes"/> as text only when they are valid UTF-8 and contain no NUL —
    /// the discriminator between "safe to carry as a string" and "must ride as base64".
    /// </summary>
    public static bool TryDecode(byte[] bytes, out string text)
    {
        try
        {
            var decoded = Strict.GetString(bytes);
            if (decoded.Contains('\0')) { text = ""; return false; }
            text = decoded;
            return true;
        }
        catch (ArgumentException)
        {
            text = "";
            return false;
        }
    }
}
