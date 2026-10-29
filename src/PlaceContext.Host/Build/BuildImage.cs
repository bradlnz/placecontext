using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using PlaceContext.Application;
using PlaceContext.Application.Build;
using PlaceContext.Application.Ports;

namespace PlaceContext.Host.Build;

public sealed record BuildImage(string Name, string ContentType, byte[] Bytes)
{
    public string DataUrl => $"data:{ContentType};base64,{Convert.ToBase64String(Bytes)}";
    public void Validate()
    {
        if (Bytes.Length is < 12 or > 2_000_000) throw new ArgumentException("Each image must be no larger than 2 MB.");
        var valid = ContentType switch
        {
            "image/png" => Bytes.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
            "image/jpeg" => Bytes[0] == 255 && Bytes[1] == 216 && Bytes[2] == 255,
            "image/webp" => Bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8) && Bytes.AsSpan(8, 4).SequenceEqual("WEBP"u8),
            _ => false,
        };
        if (!valid) throw new ArgumentException("Upload a PNG, JPEG, or WebP image with matching file contents.");
    }
}
