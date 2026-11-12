using Microsoft.JSInterop;

namespace PlaceContext.Host.Components.ViewModels;

public sealed partial class BuildChatViewModel
{
    public bool CanShareDemo { get; private set; }
    public bool ShowDemoShare { get; private set; }
    public bool SharingDemo { get; private set; }
    public int DemoLifetimeDays { get; set; } = 7;
    public string? DemoUrl { get; private set; }
    public DateTimeOffset? DemoExpiry { get; private set; }
    public string? DemoError { get; private set; }

    public async Task OpenDemoShareAsync()
    {
        if (Conversation.Messages.Count == 0 || conversationShares is null)
        {
            return;
        }
        ShowDemoShare = true;
        var id = Conversation.Id;
        DemoError = DemoUrl = null;
        DemoExpiry = null;
        try
        {
            var expiry = await conversationShares.GetExpiryAsync(Conversation.ProjectId, id);
            if (Conversation.Id == id)
            {
                DemoExpiry = expiry;
            }
        }
        catch (Exception ex)
        {
            DemoError = ex.Message;
        }
    }

    public async Task CreateDemoAsync()
    {
        if (SharingDemo || Conversation.Messages.Count == 0 || conversationShares is null || navigation is null)
        {
            return;
        }
        var id = Conversation.Id;
        SharingDemo = true;
        DemoError = null;
        try
        {
            var link = await conversationShares.CreateAsync(Conversation.ProjectId, id, PreviewRun?.Id,
                PreviewRunArtifacts.Select(a => a.Id).Concat(PreviewArtifact is { } file ? [file.Id] : []).Distinct().ToArray(), DemoLifetimeDays);
            if (Conversation.Id == id)
            {
                DemoUrl = navigation.ToAbsoluteUri($"/share/conversations/{link.Token}").AbsoluteUri;
                DemoExpiry = link.ExpiresAt;
            }
        }
        catch (Exception ex)
        {
            DemoError = ex.Message;
        }
        finally
        {
            SharingDemo = false;
        }
    }

    public async Task RevokeDemoAsync()
    {
        if (SharingDemo || conversationShares is null)
        {
            return;
        }
        var id = Conversation.Id;
        SharingDemo = true;
        try
        {
            await conversationShares.RevokeAsync(Conversation.ProjectId, id);
            DemoUrl = null;
            DemoExpiry = null;
            DemoError = null;
        }
        catch (Exception ex)
        {
            DemoError = ex.Message;
        }
        finally
        {
            SharingDemo = false;
        }
    }

    public void CloseDemoShare() { if (!SharingDemo) ShowDemoShare = false; }

    public async Task CopyDemoAsync()
    {
        try
        {
            await js.InvokeVoidAsync("navigator.clipboard.writeText", DemoUrl);
        }
        catch (Exception)
        {
            DemoError = "Could not copy automatically. Select and copy the link below.";
        }
    }
}
