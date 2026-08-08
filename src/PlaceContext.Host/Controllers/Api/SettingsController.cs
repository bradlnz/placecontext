using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application.Ports;
using PlaceContext.Host.Auth;
using PlaceContext.Host.Branding;
using PlaceContext.Host.Controllers.Api.Records;
using PlaceContext.Infrastructure.Tenancy;

namespace PlaceContext.Host.Controllers.Api;

[ApiController]
[Route("api/v1/settings")]
[Authorize(
    AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme,
    Policy = Policies.DefaultAdmin)]
[Produces("application/json")]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class SettingsController(
    BrandingService branding,
    ITenantStore tenants,
    ICurrentTenant tenant,
    IMenuConfigService menu,
    IArtifactViewConfigService artifactViews) : ControllerBase
{
    private static readonly IReadOnlyDictionary<string, string> MenuLabels =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["dashboard"] = "Dashboard",
            ["jobs"] = "Jobs",
            ["tests"] = "Tests",
            ["chains"] = "Chains",
            ["schedules"] = "Schedules",
            ["data"] = "Data",
            ["project.entities"] = "Business",
            ["project.entities.registry"] = "Entities",
            ["vault"] = "Vault",
            ["project.events"] = "Events",
            ["chat"] = "Chat",
            ["artifacts"] = "Artifacts",
            ["observability"] = "Observability",
            ["sec-workspace"] = "Workspace (section)",
            ["overview"] = "Projects overview",
            ["wiki"] = "Wiki",
            ["settings"] = "Settings",
            ["about"] = "About",
        };

    [HttpGet("branding")]
    public async Task<ActionResult<TenantBranding>> GetBranding(CancellationToken cancellationToken)
        => Ok(await branding.GetAsync(cancellationToken));

    [HttpPut("branding")]
    public async Task<ActionResult<TenantBranding>> PutBranding(
        [FromBody] TenantBranding value,
        CancellationToken cancellationToken)
    {
        await branding.SetAsync(value, cancellationToken);
        return Ok(await branding.GetAsync(cancellationToken));
    }

    [HttpPost("branding/reset")]
    public async Task<ActionResult<TenantBranding>> ResetBranding(CancellationToken cancellationToken)
    {
        await branding.SetAsync(new TenantBranding(), cancellationToken);
        return Ok(new TenantBranding());
    }

    [HttpGet("locality")]
    public ActionResult<LocalitySettingsResponse> GetLocality()
        => Ok(ToLocalityResponse(tenant.TimeZoneId));

    [HttpPut("locality")]
    public async Task<ActionResult<LocalitySettingsResponse>> PutLocality(
        [FromBody] UpdateLocalityRequest request,
        CancellationToken cancellationToken)
    {
        var timeZoneId = request.TimeZoneId.Trim();
        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            return BadRequest(new { error = $"Unknown timezone: {timeZoneId}" });
        }
        catch (InvalidTimeZoneException)
        {
            return BadRequest(new { error = $"Unknown timezone: {timeZoneId}" });
        }

        await tenants.SetTimeZoneAsync(tenant.TenantId, timeZoneId, cancellationToken);
        return Ok(ToLocalityResponse(timeZoneId));
    }

    [HttpGet("artifacts")]
    public async Task<ActionResult<ArtifactViewConfig>> GetArtifactFilters(CancellationToken cancellationToken)
        => Ok(await artifactViews.GetAsync(cancellationToken));

    [HttpPut("artifacts")]
    public async Task<ActionResult<ArtifactViewConfig>> PutArtifactFilters(
        [FromBody] ArtifactViewConfig value,
        CancellationToken cancellationToken)
    {
        if (value.Categories.Any(category =>
            string.IsNullOrWhiteSpace(category.Label)
            || category.Prefixes.Count == 0
            || category.Prefixes.Any(string.IsNullOrWhiteSpace)))
            return BadRequest(new { error = "Every filter needs a button label and at least one filename prefix." });

        await artifactViews.SaveAsync(value, cancellationToken);
        return Ok(await artifactViews.GetAsync(cancellationToken));
    }

    [HttpPost("artifacts/reset")]
    public ActionResult<ArtifactViewConfig> ResetArtifactFilters()
        => Ok(artifactViews.DefaultConfig());

    [HttpGet("menu")]
    public async Task<ActionResult<MenuSettingsResponse>> GetMenu(CancellationToken cancellationToken)
        => Ok(ToMenuResponse(
            menu.DefaultLayout(),
            await menu.GetLayoutAsync(cancellationToken)));

    [HttpPut("menu")]
    public async Task<ActionResult<MenuSettingsResponse>> PutMenu(
        [FromBody] UpdateMenuRequest request,
        CancellationToken cancellationToken)
    {
        var layout = new MenuLayout(request.Workspace
            .Select((item, index) => new MenuItemOverride(
                item.Id,
                string.IsNullOrWhiteSpace(item.Label) ? null : item.Label.Trim(),
                index * 10,
                item.Visible,
                string.IsNullOrWhiteSpace(item.Section) ? null : item.Section.Trim()))
            .ToList());
        await menu.SaveLayoutAsync(layout, cancellationToken);
        return Ok(ToMenuResponse(menu.DefaultLayout(), layout));
    }

    [HttpPost("menu/reset")]
    public async Task<ActionResult<MenuSettingsResponse>> ResetMenu(CancellationToken cancellationToken)
    {
        var defaults = menu.DefaultLayout();
        await menu.SaveLayoutAsync(defaults, cancellationToken);
        return Ok(ToMenuResponse(defaults, defaults));
    }

    private static MenuSettingsResponse ToMenuResponse(MenuLayout defaults, MenuLayout current)
    {
        var currentById = current.Workspace.ToDictionary(item => item.Id, StringComparer.Ordinal);
        var items = defaults.Workspace
            .OrderBy(item => item.Order)
            .Select(item =>
            {
                currentById.TryGetValue(item.Id, out var configured);
                return new MenuSettingsItemResponse(
                    item.Id,
                    MenuLabels.GetValueOrDefault(item.Id, item.Id),
                    configured?.Label ?? item.Label ?? string.Empty,
                    configured?.Order ?? item.Order,
                    configured?.Visible ?? item.Visible,
                    configured?.Section ?? item.Section ?? string.Empty);
            })
            .OrderBy(item => item.Order)
            .ToList();
        return new MenuSettingsResponse(items);
    }

    private static LocalitySettingsResponse ToLocalityResponse(string timeZoneId)
        => new(
            timeZoneId,
            TimeZoneInfo.GetSystemTimeZones()
                .Select(zone => zone.Id)
                .Order(StringComparer.Ordinal)
                .ToArray());
}
