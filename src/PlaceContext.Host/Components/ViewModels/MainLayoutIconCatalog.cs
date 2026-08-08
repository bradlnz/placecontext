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
        return resolved switch
        {
            "grid" =>
                "<svg width='16' height='16' viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='1.7'><rect x='3' y='3' width='7' height='7' rx='1.5'></rect><rect x='14' y='3' width='7' height='7' rx='1.5'></rect><rect x='14' y='14' width='7' height='7' rx='1.5'></rect><rect x='3' y='14' width='7' height='7' rx='1.5'></rect></svg>",
            "dashboard" =>
                "<svg width='16' height='16' viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='1.7'><rect x='3' y='3' width='7' height='7' rx='1.5'></rect><rect x='14' y='3' width='7' height='7' rx='1.5'></rect><rect x='14' y='14' width='7' height='7' rx='1.5'></rect><rect x='3' y='14' width='7' height='7' rx='1.5'></rect></svg>",
            "rocket" =>
                "<svg width='16' height='16' viewBox='0 0 24 24' fill='none' stroke='currentColor'><path d='M9 11a14 14 0 0 1 7-8c2.6 0 4 1.4 4 4a14 14 0 0 1-8 7l-3-3z'></path></svg>",
            "users" =>
                "<svg width='16' height='16' viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='1.7' stroke-linecap='round' stroke-linejoin='round'><path d='M7 21a4 4 0 0 1 4-4h2a4 4 0 0 1 4 4'></path><circle cx='9' cy='8' r='3'></circle><path d='M17 11a3 3 0 0 1 0 6'></path><circle cx='15' cy='8' r='3'></circle></svg>",
            "box" =>
                "<svg width='16' height='16' viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='1.7' stroke-linecap='round' stroke-linejoin='round'><path d='M4 7l8-4 8 4-8 4-8-4Z'></path><path d='M4 7v10l8 4 8-4V7'></path><path d='M12 11v10'></path></svg>",
            "test" =>
                "<svg width='16' height='16' viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='1.7' stroke-linecap='round' stroke-linejoin='round'><path d='M9 2h6'></path><path d='M10 22h4'></path><path d='M12 9v3'></path><path d='M9 6l-3 3 3 3'></path><path d='M15 6l3 3-3 3'></path><path d='M12 14v7'></path><path d='M6 12h12'></path></svg>",
            "chain" =>
                "<svg width='16' height='16' viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='1.7' stroke-linecap='round' stroke-linejoin='round'><path d='M4 12h3.5'></path><path d='M9 12h6'></path><path d='M16.5 12h3.5'></path><path d='M8 9h8L16 7V5m-4 10h0v5m0-5 4-2.5m0 0 4 2.5m-4-2.5-4 2.5M4 12h.5'></path><path d='M6.5 8.5h3m-3 7h3'></path><path d='M14.5 9h3m-3 6h3'></path></svg>",
            "crm" or "users" =>
                "<svg width='16' height='16' viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='1.7' stroke-linecap='round' stroke-linejoin='round'><path d='M12 13.5c3 0 5.5 2.5 5.5 5.5v1H6.5v-1c0-3 2.5-5.5 5.5-5.5Z'></path><circle cx='12' cy='8' r='3.2'></circle></svg>",
            "clock" =>
                "<svg width='16' height='16' viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='1.7' stroke-linecap='round' stroke-linejoin='round'><circle cx='12' cy='12' r='9'></circle><polyline points='12 7 12 12 15 15'></polyline></svg>",
            "map" =>
                "<svg width='16' height='16' viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='1.7' stroke-linecap='round' stroke-linejoin='round'><path d='M3 6l6.5-2.5L15 6l5.5-2.5V19L15 21.5 9.5 19 3 21.5z'></path><path d='M15 6v16'></path><path d='M3.4 6.2L9.5 9l5.5-2.8'></path><path d='M9.5 9v13'></path></svg>",
            "key" =>
                "<svg width='16' height='16' viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='1.7' stroke-linecap='round' stroke-linejoin='round'><circle cx='7.5' cy='16.5' r='2.5'></circle><path d='M10 16.5h9.5'></path><path d='M19.5 16.5l-1.1-1.1-2.2-2.2-1.8-1.8'></path><path d='M16.5 13.5 14 11a3 3 0 1 0-4.2 4.2'></path></svg>",
            "pulse" =>
                "<svg width='16' height='16' viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='1.7' stroke-linecap='round' stroke-linejoin='round'><path d='M3 12h4l2-4 3 8 2-4 3 4h7'></path></svg>",
            "chat" =>
                "<svg width='16' height='16' viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='1.7' stroke-linecap='round' stroke-linejoin='round'><path d='M21 15a4 4 0 0 1-4 4H7l-4 3v-18a4 4 0 0 1 4-4h10a4 4 0 0 1 4 4z'></path></svg>",
            "file" =>
                "<svg width='16' height='16' viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='1.7' stroke-linecap='round' stroke-linejoin='round'><path d='M14 2H7.5A1.5 1.5 0 0 0 6 3.5V20.5A1.5 1.5 0 0 0 7.5 22h9A1.5 1.5 0 0 0 18 20.5V8z'></path><path d='M14 2v6h6'></path><path d='M8 10h8'></path><path d='M8 14h8'></path><path d='M8 18h6'></path></svg>",
            "ledger" =>
                "<svg width='16' height='16' viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='1.7' stroke-linecap='round' stroke-linejoin='round'><path d='M4 4h16'></path><path d='M4 8h16'></path><path d='M4 12h16'></path><path d='M4 16h16'></path><path d='M4 20h16'></path><path d='M8 4v16'></path><path d='M16 4v16'></path></svg>",
            "data" =>
                "<svg width='16' height='16' viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='1.8' stroke-linecap='round' stroke-linejoin='round'><path d='M4 7h16M4 11h16M4 15h16M4 19h16'/></svg>",
            "observability" =>
                "<svg width='16' height='16' viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='1.7' stroke-linecap='round' stroke-linejoin='round'><path d='M3 3v18h18M7 15v-4M12 15V9M17 15V11'></path></svg>",
            "overview" =>
                "<svg width='16' height='16' viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='1.7' stroke-linecap='round' stroke-linejoin='round'><path d='M3 4h18M3 8h18M3 12h18M3 16h18M3 20h18'></path></svg>",
            "wiki" =>
                "<svg width='16' height='16' viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='1.7' stroke-linecap='round' stroke-linejoin='round'><path d='M6 4h8l4 4v12H6z'></path><path d='M14 4v4h4M9 12h6'/></svg>",
            "about" =>
                "<svg width='16' height='16' viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='1.7' stroke-linecap='round' stroke-linejoin='round'><circle cx='12' cy='12' r='9'/><path d='M12 10h.01M12 14h.01M9 8h6'/></svg>",
            "settings" =>
                "<svg width='16' height='16' viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='1.7' stroke-linecap='round' stroke-linejoin='round'><path d='M12 15a3 3 0 1 0 0-6 3 3 0 0 0 0 6Z'></path><path d='M19.4 15a1.8 1.8 0 0 0 .4-1.1v-.9a1.8 1.8 0 0 0-.4-1.1l-1.5-1.2a1.8 1.8 0 0 0-.2-2l.6-1.8a1.8 1.8 0 0 0-1-2h-1.6a1.8 1.8 0 0 0-1.3.3l-1.9 1.2a1.8 1.8 0 0 0-1.9 0l-1.9-1.2a1.8 1.8 0 0 0-1.3-.3h-1.6a1.8 1.8 0 0 0-1 2l.6 1.8a1.8 1.8 0 0 0-.2 2l-1.5 1.2A1.8 1.8 0 0 0 4 12.9v1.8a1.8 1.8 0 0 0 .4 1.1l1.5 1.2a1.8 1.8 0 0 0 .2 2l-.6 1.8a1.8 1.8 0 0 0 1 2h1.6a1.8 1.8 0 0 0 1.3-.3l1.9-1.2a1.8 1.8 0 0 0 1.9 0l1.9 1.2a1.8 1.8 0 0 0 1.3.3h1.6a1.8 1.8 0 0 0 1-2l-.6-1.8a1.8 1.8 0 0 0 .2-2z'/></svg>",
            _ =>
                "<svg width='16' height='16' viewBox='0 0 24 24' fill='none' stroke='currentColor'><rect x='3' y='4' width='18' height='16' rx='2'></rect><path d='M7 9l3 2.5L7 14'></path><path d='M13 14.5h4'></path></svg>",
        };
    }
}
