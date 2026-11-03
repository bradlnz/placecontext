using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Jobs;
namespace PlaceContext.Host.Components.ViewModels;

public sealed class ParamInputViewModel(
    JobInputFileService uploads
) : PageViewModel, IComponentViewModel
{
    private const long MaxFileBytes = JobInputFileService.MaxFileBytes;
    private JobParameterDto _parameter = default!;
    private Guid _projectId;
    private string _value = string.Empty;
    public JobParameterDto Parameter => _parameter;
    public Guid ProjectId => _projectId;
    public string Value => _value;
    public ParameterInputType InputType => ParameterInputCatalog.Parse(_parameter.Type);
    public bool Uploading
    {
        get; private set;
    }
    public string? UploadError
    {
        get; private set;
    }
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
        if (!uploads.IsEnabled)
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
            await using var stream = file.OpenReadStream(MaxFileBytes);
            var marker = await uploads.UploadAsync(_projectId, file.Name, file.ContentType, file.Size, stream);
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
        {
            return null;
        }
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

}
