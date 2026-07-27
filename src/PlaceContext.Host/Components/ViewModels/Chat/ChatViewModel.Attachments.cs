using PlaceContext.Application;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Mcp;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;
using PlaceContext.Infrastructure.Chat;
using PlaceContext.Infrastructure.Caching;

namespace PlaceContext.Host.Components.ViewModels;

public sealed partial class ChatViewModel
{
    // ── Attachments ──────────────────────────────────────────────────────────

    public string? ExtractText(byte[] data, string name) => _docExtractor.ExtractText(data, name);

    public void SetAttachment(byte[] data, string name, string? extractedText)
    {
        AttachedFile = data;
        AttachedFileName = name;
        AttachedFileText = extractedText;
        NotifyStateChanged();
    }

    public void RemoveAttachment()
    {
        AttachedFile = null;
        AttachedFileName = null;
        AttachedFileText = null;
        NotifyStateChanged();
    }

}
