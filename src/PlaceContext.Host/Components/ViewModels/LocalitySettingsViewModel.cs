using Microsoft.AspNetCore.Components;
using PlaceContext.Application.Ports;
using PlaceContext.Infrastructure.Tenancy;

namespace PlaceContext.Host.Components.ViewModels;

public sealed class LocalitySettingsViewModel(
    ITenantStore tenants,
    ICurrentTenant tenant,
    PortalUiState ui,
    NavigationManager navigation
) : PageViewModel
{
    private static class Copy
    {
        public const string Title = "Locality";
        public const string Subtitle = "workspace timezone — schedules and displayed times obey it";
        public const string Saving = "Saving…";
        public const string Saved = "Saved — reloading…";
        public const string UnknownTimezone = "Unknown timezone id '{0}'.";
    }

    public string TimeZoneId { get; set; } = "UTC";
    public bool Busy { get; private set; }
    public string? Message { get; private set; }

    public void Initialize()
    {
        TimeZoneId = tenant.TimeZoneId;
        ui.Set(Copy.Title, Copy.Subtitle);
    }

    public string PreviewNow()
    {
        try
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId.Trim());
            return TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone).ToString("ddd HH:mm");
        }
        catch (TimeZoneNotFoundException)
        {
            return "unknown timezone";
        }
        catch (InvalidTimeZoneException)
        {
            return "unknown timezone";
        }
    }

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        Busy = true;
        Message = null;
        try
        {
            var timeZoneId = TimeZoneId.Trim();
            _ = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            await tenants.SetTimeZoneAsync(tenant.TenantId, timeZoneId, cancellationToken);
            Message = Copy.Saved;
            navigation.NavigateTo(PageRoutes.LocalitySettings, forceLoad: true);
        }
        catch (TimeZoneNotFoundException)
        {
            Message = string.Format(Copy.UnknownTimezone, TimeZoneId.Trim());
        }
        catch (InvalidTimeZoneException)
        {
            Message = string.Format(Copy.UnknownTimezone, TimeZoneId.Trim());
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
