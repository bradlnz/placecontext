using PlaceContext.Application.Ports;
using PlaceContext.Host;

namespace PlaceContext.Host.Components.ViewModels;

public sealed class ApiTokensSettingsViewModel(IUserApiTokenService tokens, PortalUiState ui)
    : PageViewModel
{
    public const string DefaultLifetimeDays = "90";
    public IReadOnlyList<UserApiTokenView> Tokens { get; private set; } =
        Array.Empty<UserApiTokenView>();
    public bool Loading { get; private set; } = true;
    public bool Busy { get; private set; }
    public string? Message { get; private set; }
    public string? CreateError { get; private set; }
    public string NewName { get; set; } = "";
    public string LifetimeDays { get; set; } = DefaultLifetimeDays;
    public string? CreatedRaw { get; private set; }
    public string? CreatedPrefix { get; private set; }

    public async Task LoadAsync()
    {
        ui.Set("API tokens", "personal tokens for project data and search");
        Loading = true;
        NotifyStateChanged();
        try
        {
            Tokens = await tokens.ListMineAsync();
        }
        catch (Exception ex)
        {
            Message = ex.Message;
        }
        finally
        {
            Loading = false;
            NotifyStateChanged();
        }
    }

    public void DismissCreated()
    {
        CreatedRaw = null;
        NotifyStateChanged();
    }

    public async Task CreateAsync()
    {
        CreateError = null;
        CreatedRaw = null;
        if (string.IsNullOrWhiteSpace(NewName))
        {
            CreateError = "Give the token a name.";
            return;
        }
        Busy = true;
        NotifyStateChanged();
        try
        {
            var days =
                int.TryParse(LifetimeDays, out var parsed) && parsed > 0
                    ? parsed
                    : int.Parse(DefaultLifetimeDays);
            var created = await tokens.CreateAsync(NewName.Trim(), TimeSpan.FromDays(days));
            CreatedRaw = created.RawToken;
            CreatedPrefix = created.TokenPrefix;
            NewName = "";
            await LoadAsync();
        }
        catch (Exception ex)
        {
            CreateError = ex.Message;
        }
        finally
        {
            Busy = false;
            NotifyStateChanged();
        }
    }

    public async Task RevokeAsync(Guid id)
    {
        Busy = true;
        NotifyStateChanged();
        try
        {
            await tokens.RevokeAsync(id);
            Message = "Token revoked.";
            await LoadAsync();
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
