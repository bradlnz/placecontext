using System.Text;
using Microsoft.JSInterop;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Host.Components.ViewModels;

public sealed class ArtifactDownloadViewModel(IJSRuntime js) : PageViewModel, IComponentViewModel
{
    public bool Busy { get; private set; }
    public string? Error { get; private set; }

    public async Task DownloadAsync(Func<RunArtifactView> getArtifact)
    {
        if (Busy) return;
        Busy = true;
        Error = null;
        try
        {
            var artifact = getArtifact();
            // shortcut: downloads buffer retained content on demand; use HTTP streaming for files that outgrow memory.
            var bytes = artifact.IsBinary ? Convert.FromBase64String(artifact.Content) : Encoding.UTF8.GetBytes(artifact.Content);
            using var stream = new DotNetStreamReference(new MemoryStream(bytes));
            await js.InvokeVoidAsync("pcdata.downloadStream", artifact.Name, stream);
        }
        catch (Exception ex) when (ex is JSException or FormatException or TaskCanceledException)
        {
            Error = "Download failed. Please try again.";
        }
        finally { Busy = false; }
    }
}
