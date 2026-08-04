using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using PlaceContext.Host;
using PlaceContext.Host.Branding;

namespace PlaceContext.Host.Components.ViewModels;

public sealed class BrandingSettingsViewModel(
    BrandingService branding,
    PortalUiState ui,
    NavigationManager navigation
) : PageViewModel
{
    public const long MaxLogoBytes = 200 * 1024;
    public const string PageRoute = "/settings/branding";
    public const string SavedMessage = "Saved — reloading…";
    public string Name { get; set; } = "";
    public string? Logo { get; private set; }
    public string Background { get; set; } = "#0a0c0e";
    public string Panel { get; set; } = "#0d1013";
    public string Text { get; set; } = "#e6edf3";
    public string Accent { get; set; } = "#43d675";
    public bool Busy { get; private set; }
    public string? Message { get; private set; }

    public async Task LoadAsync()
    {
        ui.Set("Branding", "whitelabel the portal for this workspace");
        var value = await branding.GetAsync();
        Name = value.ProductName ?? "";
        Logo = value.LogoDataUri;
        Background = value.BgColor ?? Background;
        Panel = value.PanelColor ?? Panel;
        Text = value.TextColor ?? Text;
        Accent = value.AccentColor ?? Accent;
        NotifyStateChanged();
    }

    public async Task SetLogoAsync(InputFileChangeEventArgs args)
    {
        Message = null;
        var file = args.File;
        if (file.Size > MaxLogoBytes)
        {
            Message = "Logo too large — keep it under 200 KB.";
            return;
        }
        using var stream = new MemoryStream();
        await file.OpenReadStream(MaxLogoBytes).CopyToAsync(stream);
        Logo = $"data:{file.ContentType};base64,{Convert.ToBase64String(stream.ToArray())}";
        NotifyStateChanged();
    }

    public void RemoveLogo()
    {
        Logo = null;
        NotifyStateChanged();
    }

    public async Task SaveAsync()
    {
        Busy = true;
        Message = null;
        NotifyStateChanged();
        try
        {
            await branding.SetAsync(
                new TenantBranding(
                    string.IsNullOrWhiteSpace(Name) ? null : Name.Trim(),
                    Logo,
                    Background,
                    Panel,
                    Text,
                    Accent
                )
            );
            Message = SavedMessage;
            navigation.NavigateTo(PageRoute, forceLoad: true);
        }
        catch (Exception ex)
        {
            Message = ex.Message;
        }
        finally
        {
            Busy = false;
            NotifyStateChanged();
        }
    }

    public async Task ResetAsync()
    {
        Busy = true;
        NotifyStateChanged();
        try
        {
            await branding.SetAsync(new TenantBranding());
            navigation.NavigateTo(PageRoute, forceLoad: true);
        }
        catch (Exception ex)
        {
            Message = ex.Message;
        }
        finally
        {
            Busy = false;
            NotifyStateChanged();
        }
    }
}
