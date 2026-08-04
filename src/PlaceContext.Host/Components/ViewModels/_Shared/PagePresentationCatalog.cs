using System.Globalization;
using Microsoft.AspNetCore.Components;
using PlaceContext.Application.Dtos;
using PlaceContext.Host;

namespace PlaceContext.Host.Components.ViewModels;

/// <summary>Shared typed presentation policy consumed by page ViewModels.</summary>
public sealed class PagePresentationCatalog
{
    public string Date(DateTimeOffset value) =>
        value.ToWorkspaceTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public string DateTime(DateTimeOffset value) =>
        value.ToWorkspaceTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    public string ShortDateTime(DateTimeOffset value) =>
        value.ToWorkspaceTime().ToString("MMM d · HH:mm", CultureInfo.InvariantCulture);

    public string Month(DateTime value) =>
        value.ToString("MMMM yyyy", CultureInfo.InvariantCulture);

    public string Number(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

    public string Number(double value) => value.ToString("N0", CultureInfo.InvariantCulture);

    public string Time(DateTimeOffset value) =>
        value.ToWorkspaceTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture);

    public string TimeWithMonth(DateTimeOffset value) =>
        value.ToWorkspaceTime().ToString("HH:mm · MMM d", CultureInfo.InvariantCulture);

    public string Iso(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);

    public string Format(DateTimeOffset value, string format) =>
        value.ToWorkspaceTime().ToString(format, CultureInfo.InvariantCulture);

    public string FormatDate(DateTime value) =>
        value.ToString("MMMM yyyy", CultureInfo.InvariantCulture);

    public string EnumValue<T>(T value)
        where T : struct, Enum => value.ToString();

    public string AbbreviatedId(Guid value) => value.ToString("N")[..8];

    public string Id(Guid value) => value.ToString();

    public bool IsExpired(DateTimeOffset? value) =>
        value.HasValue && value.Value <= DateTimeOffset.UtcNow;

    public bool TrySvg(string? artifact, out string svg) => ArtifactChart.TrySvg(artifact, out svg);

    public string Bytes(long value) => Helpers.FormatHelper.Bytes(value);

    public string Duration(DateTimeOffset start, DateTimeOffset end) =>
        Helpers.FormatHelper.Duration(start, end);

    public string Milliseconds(double? value) => Helpers.FormatHelper.Ms(value);

    public string Json(string raw) => Helpers.FormatHelper.PrettyJson(raw);

    public string DataUri(RunArtifactView artifact) => Helpers.FormatHelper.DataUri(artifact);

    public string StatusColor(string? status) => Helpers.StatusHelper.Color(status);

    public string StatusBackground(string? status) => Helpers.StatusHelper.Background(status);

    public string StatusLabel(string? status) => string.IsNullOrWhiteSpace(status) ? "—" : status;

    public string UpperStatus(string? status) => StatusLabel(status).ToUpperInvariant();

    public bool IsRunning(string? status) =>
        string.Equals(status, "Running", StringComparison.OrdinalIgnoreCase);

    public bool IsJson(string? contentType) =>
        contentType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true;

    public bool IsCsv(string? contentType) =>
        contentType?.Contains("csv", StringComparison.OrdinalIgnoreCase) == true;

    public string FileExtensionLabel(string fileName)
    {
        var extension = Path.GetExtension(fileName).TrimStart('.');
        return string.IsNullOrWhiteSpace(extension)
            ? "FILE"
            : extension[..Math.Min(4, extension.Length)].ToUpperInvariant();
    }

    public RenderFragment FileIcon(string contentType, int size) =>
        ArtifactsViewModel.FileIcon(contentType, size);
}
