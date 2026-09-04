using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using PlaceContext.Application;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Host.Branding;
using PlaceContext.Infrastructure.Operations;

namespace PlaceContext.Host.Components.ViewModels;

internal static class MainLayoutIconCatalog
{
    public static string Markup(string? kind)
    {
        var resolved = kind?.Trim().ToLowerInvariant();
        var body = resolved switch
        {
            "grid" => "<rect x='3' y='3' width='7' height='7' rx='1.5'/><rect x='14' y='3' width='7' height='7' rx='1.5'/><rect x='14' y='14' width='7' height='7' rx='1.5'/><rect x='3' y='14' width='7' height='7' rx='1.5'/>",
            "dashboard" => "<rect x='3' y='3' width='7' height='9' rx='1.5'/><rect x='14' y='3' width='7' height='5' rx='1.5'/><rect x='14' y='11' width='7' height='10' rx='1.5'/><rect x='3' y='15' width='7' height='6' rx='1.5'/>",
            "rocket" => "<path d='M14 4.1c2.3-1.5 4.8-1.3 5.9-1 .3 1.1.5 3.6-1 5.9l-5.4 5.4-3.9.6-.6-3.9L14 4.1Z'/><circle cx='16' cy='7' r='1.5'/><path d='M9.5 7.8 5.8 8.9 3 11.7l6 .3M16.2 14.5 15.1 18.2 12.3 21l-.3-6M6.5 15.5l-2 4 4-2'/>",
            "users" => "<path d='M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2'/><circle cx='9' cy='7' r='4'/><path d='M22 21v-2a4 4 0 0 0-3-3.87M16 3.13a4 4 0 0 1 0 7.75'/>",
            "box" => "<path d='m21 8-9 5-9-5 9-5 9 5Z'/><path d='m3 8 9 5 9-5v8l-9 5-9-5V8Z'/><path d='M12 13v8'/>",
            "test" => "<path d='M9 3h6M10 3v6l-5 9a2 2 0 0 0 1.7 3h10.6A2 2 0 0 0 19 18l-5-9V3'/><path d='M8 14h8'/>",
            "chain" => "<rect x='3' y='5' width='6' height='5' rx='1.5'/><rect x='15' y='14' width='6' height='5' rx='1.5'/><path d='M9 7.5h4a3 3 0 0 1 3 3V14M12 7.5l2-2M12 7.5l2 2'/>",
            "clock" => "<circle cx='12' cy='12' r='9'/><path d='M12 7v5l3 2'/>",
            "map" => "<polygon points='3 6 9 3 15 6 21 3 21 18 15 21 9 18 3 21 3 6'/><path d='M9 3v15M15 6v15'/>",
            "key" => "<circle cx='8' cy='15' r='4'/><path d='m11 12 8-8M15 8l3 3M17 6l2 2'/>",
            "pulse" => "<path d='M3 12h4l2.5-5 5 10 2.5-5h4'/>",
            "file" => "<path d='M14 2H7a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V7l-5-5Z'/><path d='M14 2v5h5M9 13h6M9 17h6'/>",
            "ledger" => "<rect x='4' y='3' width='16' height='18' rx='2'/><path d='M8 3v18M12 8h5M12 12h5M12 16h3'/>",
            "data" or "data.tables" => "<ellipse cx='12' cy='5' rx='8' ry='3'/><path d='M4 5v6c0 1.7 3.6 3 8 3s8-1.3 8-3V5M4 11v6c0 1.7 3.6 3 8 3s8-1.3 8-3v-6'/>",
            "data.analytics" => "<path d='M4 20V10M10 20V4M16 20v-7M22 20H2'/>",
            "data.search" => "<circle cx='11' cy='11' r='7'/><path d='m20 20-4-4'/>",
            "data.datamap" => "<circle cx='5' cy='6' r='2'/><circle cx='19' cy='6' r='2'/><circle cx='12' cy='18' r='2'/><path d='M7 6h10M6 8l5 8M18 8l-5 8'/>",
            "data.entities" => "<rect x='3' y='3' width='7' height='7' rx='1.5'/><rect x='14' y='3' width='7' height='7' rx='1.5'/><rect x='8.5' y='14' width='7' height='7' rx='1.5'/><path d='M6.5 10v2h11v-2M12 12v2'/>",
            "data.graph" => "<circle cx='5' cy='12' r='2.5'/><circle cx='18.5' cy='5' r='2.5'/><circle cx='18.5' cy='19' r='2.5'/><path d='m7.3 10.8 9-4.6M7.3 13.2l9 4.6'/>",
            "observability" => "<path d='M3 12h4l2.5-5 5 10 2.5-5h4'/><circle cx='12' cy='12' r='10'/>",
            "overview" => "<rect x='3' y='4' width='18' height='16' rx='2'/><path d='M3 9h18M8 9v11'/>",
            "wiki" => "<path d='M4 5a3 3 0 0 1 3-2h5v18H7a3 3 0 0 0-3 2V5ZM20 5a3 3 0 0 0-3-2h-5v18h5a3 3 0 0 1 3 2V5Z'/>",
            "about" => "<circle cx='12' cy='12' r='9'/><path d='M12 11v5M12 8h.01'/>",
            "settings" => "<path d='M4 6h10M18 6h2M4 12h2M10 12h10M4 18h7M15 18h5'/><circle cx='16' cy='6' r='2'/><circle cx='8' cy='12' r='2'/><circle cx='13' cy='18' r='2'/>",
            _ => "<rect x='3' y='4' width='18' height='16' rx='2'/><path d='m7 9 3 3-3 3M13 15h4'/>",
        };

        return $"<svg class='nav-icon' width='16' height='16' viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='1.8' stroke-linecap='round' stroke-linejoin='round' aria-hidden='true' focusable='false'>{body}</svg>";
    }
}
