using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Host.Components.ViewModels.Crm;

public static class CrmPresentationCatalog
{
    public static readonly CrmSectionPresentation[] Sections =
    [
        new(
            CrmSection.Conversations,
            "Conversations",
            "Keep customer email, SMS, and internal notes in one shared timeline.",
            false,
            ""
        ),
        new(
            CrmSection.Calendars,
            "Calendars",
            "Manage customer appointments and connected calendars.",
            false,
            ""
        ),
        new(
            CrmSection.Contacts,
            "Contacts",
            "Manage customer contact details and relationship context.",
            true,
            "Add contact"
        ),
        new(
            CrmSection.Opportunities,
            "Opportunities",
            "Move customer opportunities through the full lifecycle pipeline.",
            true,
            "Add contact"
        ),
        new(
            CrmSection.Automations,
            "Automations",
            "Connect customer lifecycle events to durable job-chain workflows.",
            true,
            "Add automation"
        ),
        new(
            CrmSection.Portals,
            "Customer portals",
            "Provision, configure, and manage portal access for selected CRM clients.",
            false,
            ""
        ),
        new(
            CrmSection.Settings,
            "CRM settings",
            "Connect external lead forms without exposing the rest of your CRM.",
            false,
            ""
        ),
    ];

    public static readonly CrmCommunicationChannel[] Channels =
    [
        CrmCommunicationChannel.Note,
        CrmCommunicationChannel.Email,
        CrmCommunicationChannel.Sms,
    ];

    public static CrmSection ParseSection(string value) =>
        value switch
        {
            "conversations" => CrmSection.Conversations,
            "calendars" => CrmSection.Calendars,
            "contacts" => CrmSection.Contacts,
            "opportunities" => CrmSection.Opportunities,
            "automations" => CrmSection.Automations,
            "portals" => CrmSection.Portals,
            _ => CrmSection.Settings,
        };

    public static string SectionKey(CrmSection value) =>
        value switch
        {
            CrmSection.Conversations => "conversations",
            CrmSection.Calendars => "calendars",
            CrmSection.Contacts => "contacts",
            CrmSection.Opportunities => "opportunities",
            CrmSection.Automations => "automations",
            CrmSection.Portals => "portals",
            _ => "settings",
        };

    public static CrmDetailTab ParseDetailTab(string value) =>
        value switch
        {
            "comms" => CrmDetailTab.Communications,
            "artifacts" => CrmDetailTab.Artifacts,
            _ => CrmDetailTab.Overview,
        };

    public static string DetailTabKey(CrmDetailTab value) =>
        value switch
        {
            CrmDetailTab.Communications => "comms",
            CrmDetailTab.Artifacts => "artifacts",
            _ => "overview",
        };

    public static CrmArtifactSource ParseArtifactSource(string value) =>
        value switch
        {
            "upload" => CrmArtifactSource.Upload,
            "automation" => CrmArtifactSource.Automation,
            _ => CrmArtifactSource.All,
        };

    public static string ArtifactSourceKey(CrmArtifactSource value) =>
        value switch
        {
            CrmArtifactSource.Upload => "upload",
            CrmArtifactSource.Automation => "automation",
            _ => "all",
        };

    public static CrmCommunicationChannel ParseChannel(string value) =>
        Enum.TryParse<CrmCommunicationChannel>(value, true, out var result)
            ? result
            : CrmCommunicationChannel.Note;

    public static CrmCommunicationStatus ParseStatus(string value) =>
        Enum.TryParse<CrmCommunicationStatus>(value, true, out var result)
            ? result
            : CrmCommunicationStatus.Pending;

    public static string ChannelCss(CrmCommunicationChannel value) =>
        value.ToString().ToLowerInvariant();

    public static string StatusCss(CrmCommunicationStatus value) =>
        value.ToString().ToLowerInvariant();

    public static string ChannelLabel(CrmCommunicationChannel value) =>
        value == CrmCommunicationChannel.Sms ? "SMS" : value.ToString();

    public static string ChannelIcon(CrmCommunicationChannel value) =>
        value switch
        {
            CrmCommunicationChannel.Email => "mail",
            CrmCommunicationChannel.Sms => "message",
            _ => "note",
        };
}
