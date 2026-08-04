using PlaceContext.Application.Dtos;

namespace PlaceContext.Host.Components.ViewModels;

internal static class ChartPresentation
{
    public static string CanvasId(string prefix, string slot) =>
        prefix
        + Convert.ToHexString(
            System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(slot))
        )[..12];

    public static string Duration(DateTimeOffset start, DateTimeOffset? finish)
    {
        if (finish is not { } end)
            return "—";
        var span = end - start;
        return span.TotalSeconds < 60
            ? $"{(int)span.TotalSeconds}s"
            : $"{(int)span.TotalMinutes}m {span.Seconds}s";
    }
}
