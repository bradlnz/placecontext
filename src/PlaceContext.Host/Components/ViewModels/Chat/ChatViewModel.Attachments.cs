using PlaceContext.Application;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Mcp;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;
using PlaceContext.AgentChat.Infrastructure.Caching;
using PlaceContext.AgentChat.Infrastructure.Chat;

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
        AttachmentError = extractedText is null
            ? "The file is attached, but no readable text could be extracted from it."
            : null;
        NotifyStateChanged();
    }

    public void SetAttachmentError(string message)
    {
        AttachedFile = null;
        AttachedFileName = null;
        AttachedFileText = null;
        AttachmentError = message;
        NotifyStateChanged();
    }

    public void RemoveAttachment()
    {
        AttachedFile = null;
        AttachedFileName = null;
        AttachedFileText = null;
        AttachmentError = null;
        NotifyStateChanged();
    }
}
