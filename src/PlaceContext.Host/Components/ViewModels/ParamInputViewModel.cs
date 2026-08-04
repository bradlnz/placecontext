using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;

namespace PlaceContext.Host.Components.ViewModels;

public enum ParameterInputType
{
    Text,
    Select,
    Number,
    Checkbox,
    File,
}

public static class ParameterInputCatalog
{
    public static ParameterInputType Parse(string? value) =>
        value?.ToLowerInvariant() switch
        {
            "select" => ParameterInputType.Select,
            "number" => ParameterInputType.Number,
            "checkbox" => ParameterInputType.Checkbox,
            "file" => ParameterInputType.File,
            _ => ParameterInputType.Text,
        };
}

public sealed class ParamInputViewModel(
    IObjectStore objectStore,
    IHttpClientFactory httpClientFactory
) : PageViewModel, IComponentViewModel
{
    private const long MaxFileBytes = 40L * 1024 * 1024;
    private JobParameterDto _parameter = default!;
    private Guid _projectId;
    private string _value = string.Empty;
    public JobParameterDto Parameter => _parameter;
    public Guid ProjectId => _projectId;
    public string Value => _value;
    public ParameterInputType InputType => ParameterInputCatalog.Parse(_parameter.Type);
    public bool Uploading { get; private set; }
    public string? UploadError { get; private set; }
    public string Accept =>
        _parameter.Options is { Count: > 0 }
            ? string.Join(",", _parameter.Options)
            : "application/pdf,image/*";
    public string? SelectedFileName => FileMarkerFilename(_value);
    public bool CheckboxChecked => bool.TryParse(_value, out var checkedValue) && checkedValue;

    public string CheckboxValue(ChangeEventArgs args) =>
        args.Value is bool value && value
            ? bool.TrueString.ToLowerInvariant()
            : bool.FalseString.ToLowerInvariant();

    public void SetValue(string? value) => _value = value ?? string.Empty;

    public void SetParameters(JobParameterDto parameter, Guid projectId, string? value)
    {
        _parameter = parameter;
        _projectId = projectId;
        _value = value ?? string.Empty;
    }

    public async Task UploadAsync(InputFileChangeEventArgs args, EventCallback<string> valueChanged)
    {
        UploadError = null;
        var file = args.File;
        if (_projectId == Guid.Empty)
        {
            UploadError = "A project is required before uploading.";
            return;
        }
        if (!objectStore.IsEnabled)
        {
            UploadError = "File storage is not configured.";
            return;
        }
        if (file.Size <= 0 || file.Size > MaxFileBytes)
        {
            UploadError = "Choose a non-empty PDF or image no larger than 40 MB.";
            return;
        }
        Uploading = true;
        try
        {
            var filename = SanitizeFilename(file.Name);
            var contentType = ContentTypeFor(filename, file.ContentType);
            var key = $"job-inputs/{_projectId:N}/{Guid.NewGuid():N}-{filename}";
            await objectStore.EnsureBucketAsync(objectStore.ReportsBucket);
            var uploadUrl = await objectStore.PresignUploadAsync(
                objectStore.ReportsBucket,
                key,
                TimeSpan.FromMinutes(20)
            );
            using var content = new StreamContent(file.OpenReadStream(MaxFileBytes));
            content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            using var response = await httpClientFactory
                .CreateClient()
                .PutAsync(uploadUrl, content);
            response.EnsureSuccessStatusCode();
            var marker = JsonSerializer.Serialize(
                new Dictionary<string, object?>
                {
                    ["$file"] = new
                    {
                        bucket = objectStore.ReportsBucket,
                        key,
                        filename,
                        content_type = contentType,
                        size_bytes = file.Size,
                    },
                }
            );
            _value = marker;
            await valueChanged.InvokeAsync(marker);
        }
        catch (Exception exception)
        {
            UploadError = $"Upload failed: {exception.Message}";
        }
        finally
        {
            Uploading = false;
            NotifyStateChanged();
        }
    }

    private static string? FileMarkerFilename(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        try
        {
            return JsonDocument
                .Parse(value)
                .RootElement.GetProperty("$file")
                .GetProperty("filename")
                .GetString();
        }
        catch
        {
            return null;
        }
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
            return browserContentType;
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
