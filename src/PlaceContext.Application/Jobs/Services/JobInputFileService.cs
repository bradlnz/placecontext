using System.Text.Json;
using PlaceContext.Application.Artifacts;
namespace PlaceContext.Application.Jobs;

public sealed class JobInputFileService(IObjectStore objectStore, IPresignedUploadClient uploader)
{
    public const long MaxFileBytes = 40L * 1024 * 1024;
    public bool IsEnabled => objectStore.IsEnabled;

    public async Task<string> UploadAsync(Guid projectId, string name, string browserContentType, long length, Stream stream, CancellationToken ct = default)
    {
        if (projectId == Guid.Empty)
        {
            throw new ArgumentException("A project is required before uploading.");
        }
        if (!IsEnabled)
        {
            throw new InvalidOperationException("File storage is not configured.");
        }
        if (length <= 0 || length > MaxFileBytes)
        {
            throw new ArgumentException("Choose a non-empty PDF or image no larger than 40 MB.");
        }
        var filename = SanitizeFilename(name);
        var contentType = ContentTypeFor(filename, browserContentType);
        var key = $"job-inputs/{projectId:N}/{Guid.NewGuid():N}-{filename}";
        await objectStore.EnsureBucketAsync(objectStore.ReportsBucket, ct);
        var uploadUrl = await objectStore.PresignUploadAsync(
            objectStore.ReportsBucket,
            key,
            TimeSpan.FromMinutes(20)
        );
        await uploader.UploadAsync(uploadUrl, stream, length, contentType, ct);
        var marker = JsonSerializer.Serialize(
            new Dictionary<string, object?>
            {
                ["$file"] = new
                {
                    bucket = objectStore.ReportsBucket,
                    key,
                    filename,
                    content_type = contentType,
                    size_bytes = length,
                },
            }
        );
        return marker;
    }
    private static string SanitizeFilename(string name)
    {
        var leaf = Path.GetFileName(name);
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(
            leaf.Select(character => invalid.Contains(character) ? '_' : character).ToArray()
        ).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "upload.bin" : cleaned;
    }

    private static string ContentTypeFor(string filename, string browserContentType)
    {
        if (
            !string.IsNullOrWhiteSpace(browserContentType)
            && !string.Equals(
                browserContentType,
                "application/octet-stream",
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            return browserContentType;
        }
        return Path.GetExtension(filename).ToLowerInvariant() switch
        {
            ".pdf" => "application/pdf",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            ".tif" or ".tiff" => "image/tiff",
            _ => "application/octet-stream",
        };
    }
}
