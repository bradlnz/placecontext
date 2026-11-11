using Microsoft.JSInterop;

namespace PlaceContext.Host.Components.ViewModels;

public sealed partial class BuildChatViewModel
{
    public bool CanShareDemo { get; private set; }
    public bool ShowDemoShare { get; private set; }
    public bool SharingDemo { get; private set; }
    public string DemoOutcome { get; set; } = "";
    public int? DemoReplayStep { get; set; }
    public int DemoLifetimeDays { get; set; } = 7;
    public string? DemoUrl { get; private set; }
    public DateTimeOffset? DemoExpiry { get; private set; }
    public string? DemoError { get; private set; }

    public async Task OpenDemoShareAsync()
    {
        if (PreviewRun is not { FinishedAt: not null } run || demoShares is null)
        {
            return;
        }
        ShowDemoShare = true;
        DemoOutcome = run.FinalOutput ?? "";
        DemoReplayStep = null;
        DemoError = DemoUrl = null;
        DemoExpiry = null;
        try
        {
            var expiry = await demoShares.GetExpiryAsync(Conversation.ProjectId, run.Id);
            if (PreviewRun?.Id == run.Id)
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
        if (SharingDemo || PreviewRun is not { } run || demoShares is null || navigation is null)
        {
            return;
        }
        SharingDemo = true;
        DemoError = null;
        try
        {
            var link = await demoShares.CreateAsync(Conversation.ProjectId, run.Id, DemoOutcome, DemoReplayStep, DemoLifetimeDays);
            if (PreviewRun?.Id == run.Id)
            {
                DemoUrl = navigation.ToAbsoluteUri($"/share/workflows/{link.Token}").AbsoluteUri;
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
        if (SharingDemo || PreviewRun is not { } run || demoShares is null)
        {
            return;
        }
        SharingDemo = true;
        try
        {
            await demoShares.RevokeAsync(Conversation.ProjectId, run.Id);
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
