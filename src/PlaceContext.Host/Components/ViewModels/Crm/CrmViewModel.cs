using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;
using System.Text;
using PlaceContext.Application;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.Host;
using PlaceContext.Infrastructure.Crm;
using System.Text.Json;

namespace PlaceContext.Host.Components.ViewModels.Crm;

public sealed class CrmViewModel : PageViewModel
{
    private readonly IPlaceContextService Svc;
    private readonly PortalUiState Ui;
    private readonly IJSRuntime Js;
    private readonly NavigationManager Nav;
    private readonly CrmIngestionSettingsService IngestionSettingsService;
    private readonly IPermissionService Permissions;
    private bool _canManageClients;
    private bool _canRunAutomations;
    private bool _canManageAutomationCatalog;
    private bool _canAssignAutomationChains;
    private bool _canPostClientNotes;
    private bool _canUploadArtifacts;
    private bool _canManageCalendars;
    private bool _canSendCommunications;
    private bool _canManageMembers;
    private Guid _ingestionClientId;

    public CrmViewModel(
        IPlaceContextService svc,
        PortalUiState ui,
        IJSRuntime js,
        NavigationManager nav,
        CrmIngestionSettingsService ingestionSettings,
        IPermissionService permissions
    )
    {
        Svc = svc;
        Ui = ui;
        Js = js;
        Nav = nav;
        IngestionSettingsService = ingestionSettings;
        Permissions = permissions;
    }

    [Parameter]
    public Guid ProjectId { get; set; }

    public static readonly CustomerLifecycleStage[] Stages =
        Enum.GetValues<CustomerLifecycleStage>();
    public static readonly CrmAutomationEventType[] AutomationEvents =
        Enum.GetValues<CrmAutomationEventType>();
    public static readonly CrmCommunicationChannel[] CommsChannels =
        CrmPresentationCatalog.Channels;

    public string StageOption(CustomerLifecycleStage stage) => stage.ToString();

    public string AutomationStagePreview =>
        AutomationEvent == CrmAutomationEventType.IngestionReceived
            ? "Raw JSON payload"
            : AutomationStageLabel(AutomationStage?.ToString());
    public string CalendarMonthLabel => Presentation.FormatDate(CalendarMonth);

    public string ArtifactDate(DateTimeOffset value) => Presentation.Format(value, "dd MMM, HH:mm");

    public string AppointmentTime(DateTimeOffset value) => Presentation.Format(value, "h:mm tt");

    public string UpdatedDate(DateTimeOffset value) => Presentation.Format(value, "dd MMM yyyy");

    public string RunDateTime(DateTimeOffset value) =>
        Presentation.Format(value, "dd MMM yyyy, HH:mm");

    public string CrmSection = "opportunities";
    public CrmSection CurrentSection => CrmPresentationCatalog.ParseSection(CrmSection);
    public CrmSectionPresentation CurrentSectionPresentation =>
        CrmPresentationCatalog.Sections[(int)CurrentSection];
    public bool CrmNavOpen;
    private IReadOnlyList<CrmClientView> _clients = Array.Empty<CrmClientView>();
    public IReadOnlyList<CrmClientView> Clients
    {
        get => _clients;
        set
        {
            _clients = value;
            _filteredClientsCache = null;
        }
    }
    public IReadOnlyList<JobChainView> Chains = Array.Empty<JobChainView>();
    public IReadOnlyList<CrmAutomationRuleView> AutomationRules =
        Array.Empty<CrmAutomationRuleView>();
    public IReadOnlyList<CrmChainRunView> ClientRuns = Array.Empty<CrmChainRunView>();
    public IReadOnlyList<CrmCommunicationView> Communications = Array.Empty<CrmCommunicationView>();
    public IReadOnlyList<CrmClientArtifactView> ClientArtifacts =
        Array.Empty<CrmClientArtifactView>();
    public IReadOnlyList<CrmAppointmentView> Appointments = Array.Empty<CrmAppointmentView>();
    public IReadOnlyList<CrmCalendarView> Calendars = Array.Empty<CrmCalendarView>();
    public CrmCommsCapabilitiesView? CommsCapabilities;
    public bool Loading = true;
    public IReadOnlyList<CrmUserView> CrmUsers = Array.Empty<CrmUserView>();
    public bool CanManageClients => _canManageClients;
    public bool CanRunAutomationJobs => _canRunAutomations;
    public bool CanManageAutomationCatalog => _canManageAutomationCatalog;
    public bool CanPostNotes => _canPostClientNotes;
    public bool CanUploadArtifacts => _canUploadArtifacts;
    public bool CanManageCalendarEntries => _canManageCalendars;
    public bool CanAssignAutomationChains => _canAssignAutomationChains;
    public bool CanSendComms => _canSendCommunications;
    public bool CanManageMembers => _canManageMembers;
    private static string Slugify(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "tenant";

        var lowered = value.ToLowerInvariant();
        var sanitized = new StringBuilder(lowered.Length);
        var prevWasSeparator = false;

        foreach (var ch in lowered)
        {
            if (char.IsLetterOrDigit(ch))
            {
                sanitized.Append(ch);
                prevWasSeparator = false;
            }
            else if (char.IsWhiteSpace(ch) || ch is '-' or '_' or '.')
            {
                if (!prevWasSeparator)
                    sanitized.Append('-');
                prevWasSeparator = true;
            }
        }

        var slug = sanitized.ToString().Trim('-');
        return string.IsNullOrWhiteSpace(slug) ? "tenant" : slug;
    }
    public bool NotesMetadataOpen;
    public string? NotesMetadataJson;
    public bool LoadingRuns;
    public bool LoadingComms;
    public bool SendingComms;
    public bool LoadingArtifacts;
    public bool UploadingArtifact;
    public bool ShowEditor;
    public bool ShowAutomationEditor;
    public bool ShowAppointmentEditor;
    public bool ShowCalendarEditor;
    public bool Saving;
    public bool SavingAutomation;
    public bool SavingAppointment;
    public bool SavingCalendar;
    public bool Running;
    public bool ConfirmDelete;
    public string? Message;
    public string? FormError;
    public string? CommsError;
    public string? ArtifactError;
    public string? AppointmentError;
    public string? CalendarError;
    public Guid? EditAppointmentId;
    public Guid? EditCalendarId;
    public Guid? OpenCalendarId;
    public string AppointmentCalendarId = "";
    public string AppointmentTitle = "";
    public string AppointmentClientId = "";
    public string AppointmentStartsAt = "";
    public string AppointmentEndsAt = "";
    public string? AppointmentLocation;
    public string? AppointmentNotes;
    public string CalendarName = "";
    public string CalendarColor = "#4f7cff";
    public DateTime CalendarMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    public string ArtifactSearch = "";
    public string ArtifactSourceFilter = "all";
    public string DetailTab = "overview";
    public string ComposeChannel = "Note";
    public CrmDetailTab CurrentDetailTab => CrmPresentationCatalog.ParseDetailTab(DetailTab);
    public CrmArtifactSource CurrentArtifactSource =>
        CrmPresentationCatalog.ParseArtifactSource(ArtifactSourceFilter);
    public CrmCommunicationChannel CurrentComposeChannel =>
        CrmPresentationCatalog.ParseChannel(ComposeChannel);
    public bool ShowCustomerDetailPanel =>
        Selected is not null
        && CurrentSection
            is not global::PlaceContext.Host.Components.ViewModels.Crm.CrmSection.Conversations
        && !ShowEditor
        && !NotesMetadataOpen;
    public string StageFilterLabel =>
        StageFilter is { } stage ? StageLabel(stage).ToLowerInvariant() : "all";
    public string ComposeSubject = "";
    public string ComposeBody = "";
    private string _search = "";
    public string Search
    {
        get => _search;
        set
        {
            _search = value;
            _filteredClientsCache = null;
        }
    }
    public string ConversationSearch = "";
    private CustomerLifecycleStage? _stageFilter;
    public CustomerLifecycleStage? StageFilter
    {
        get => _stageFilter;
        set
        {
            _stageFilter = value;
            _filteredClientsCache = null;
        }
    }
    public CrmClientView? Selected;
    public Guid? SelectedChainId;
    public Guid? ConfirmArtifactRemoveId;
    public Guid? EditId;
    public Guid? EditAutomationId;
    public Guid? ConfirmAutomationDeleteId;
    public string EditName = "";
    public string? EditCompany;
    public string? EditEmail;
    public string? EditPhone;
    public CustomerLifecycleStage EditStage = CustomerLifecycleStage.Lead;
    public string? EditNotes;
    public string AutomationName = "";
    public CrmAutomationEventType AutomationEvent = CrmAutomationEventType.StageEntered;
    public CustomerLifecycleStage? AutomationStage;
    public Guid? AutomationChainId;
    public bool AutomationEnabled = true;
    public string? AutomationError;
    public IReadOnlyList<CrmIngestionSettingsView> IngestionSettings = Array.Empty<CrmIngestionSettingsView>();
    public bool LoadingIngestionSettings;
    public bool SavingIngestion;
    public string IngestionOrigin = "";
    public Guid IngestionClientId
    {
        get => _ingestionClientId;
        set
        {
            _ingestionClientId = value;
            var selected = SelectedIngestionSetting;
            IngestionOrigin = selected?.AllowedOrigin ?? "";
            NewIngestionToken = null;
            IngestionError = null;
            IngestionMessage = null;
        }
    }
    public CrmIngestionSettingsView? SelectedIngestionSetting =>
        IngestionSettings.FirstOrDefault(setting => setting.ClientId == IngestionClientId);
    public string? NewIngestionToken;
    public string? IngestionError;
    public string? IngestionMessage;
    public string NewCrmUserEmail = "";
    public string NewCrmUserName = "";
    public string NewCrmUserClientId = "";
    public string? NewCrmUserJoinCode;
    public string? NewCrmUserJoinLink;
    public string? NewCrmUserJoinMessage;
    public bool CreatingCrmUser;
    public string? CrmUserError;
    public HashSet<Guid> SelectedClientChainIds = new();
    public bool LoadingClientChainAssignments;
    public bool SavingClientChainAssignments;
    public string? ClientChainAssignmentMessage;
    public HashSet<Guid> SelectedClientUserIds = new();
    public bool LoadingClientUserAssignments;
    public bool SavingClientUserAssignments;
    public string? ClientUserAssignmentMessage;

    public bool HasPrimaryAction => CurrentSectionPresentation.CanAdd;
    public string PrimaryActionLabel => CurrentSectionPresentation.AddLabel;
    public bool CanUsePrimaryAction =>
        CurrentSection switch
        {
            global::PlaceContext.Host.Components.ViewModels.Crm.CrmSection.Contacts
                or global::PlaceContext.Host.Components.ViewModels.Crm.CrmSection.Opportunities => CanManageClients,
            global::PlaceContext.Host.Components.ViewModels.Crm.CrmSection.Automations => CanManageAutomationCatalog,
            _ => false,
        };

    public IEnumerable<JobChainView> RunableChains =>
        CanManageAutomationCatalog
            ? Chains
            : Chains.Where(chain => SelectedClientChainIds.Contains(chain.Id));

    public Task OpenCrmSection(CrmSection section) =>
        OpenCrmSection(CrmPresentationCatalog.SectionKey(section));

    public void SelectDetailTab(CrmDetailTab tab) =>
        DetailTab = CrmPresentationCatalog.DetailTabKey(tab);

    public void SelectArtifactSource(CrmArtifactSource source) =>
        ArtifactSourceFilter = CrmPresentationCatalog.ArtifactSourceKey(source);

    public void SelectChannel(CrmCommunicationChannel channel) => SelectChannel(channel.ToString());

    public string ChannelCss(CrmCommunicationView item) =>
        CrmPresentationCatalog.ChannelCss(CrmPresentationCatalog.ParseChannel(item.Channel));

    public string StatusCss(CrmCommunicationView item) =>
        CrmPresentationCatalog.StatusCss(CrmPresentationCatalog.ParseStatus(item.Status));

    public string ChannelIcon(CrmCommunicationView item) =>
        CrmPresentationCatalog.ChannelIcon(CrmPresentationCatalog.ParseChannel(item.Channel));

    public string ChannelLabel(CrmCommunicationView item) =>
        CrmPresentationCatalog.ChannelLabel(CrmPresentationCatalog.ParseChannel(item.Channel));

    public bool IsEmailComposer => CurrentComposeChannel == CrmCommunicationChannel.Email;

    public bool IsFailed(CrmCommunicationView item) =>
        CrmPresentationCatalog.ParseStatus(item.Status) == CrmCommunicationStatus.Failed;

    public bool IsSent(CrmCommunicationView item) =>
        CrmPresentationCatalog.ParseStatus(item.Status) == CrmCommunicationStatus.Sent;

    public string RunStatusCss(CrmChainRunView run) => run.Status.ToLowerInvariant();

    public string ArtifactSourceCss(CrmClientArtifactView artifact) =>
        artifact.Source.ToLowerInvariant();

    public bool IsAutomationArtifact(CrmClientArtifactView artifact) =>
        artifact.Source.Equals("Automation", StringComparison.OrdinalIgnoreCase);

    public bool ChannelAvailable(CrmCommunicationChannel channel) =>
        ChannelAvailable(channel.ToString());

    public string ChannelHelp(CrmCommunicationChannel channel) => ChannelHelp(channel.ToString());

    public static string ChannelLabel(CrmCommunicationChannel channel) =>
        CrmPresentationCatalog.ChannelLabel(channel);

    public static string ChannelIconName(CrmCommunicationChannel channel) =>
        CrmPresentationCatalog.ChannelIcon(channel);

    public string IngestionEndpoint => $"{Nav.BaseUri.TrimEnd('/')}/api/crm/ingest";

    private List<CrmClientView>? _filteredClientsCache;
    public ICollection<CrmClientView> FilteredClients =>
        _filteredClientsCache ??= Clients
            .Where(client =>
                (StageFilter is null || ParseStage(client.LifecycleStage) == StageFilter)
                && (
                    string.IsNullOrWhiteSpace(Search)
                    || Searchable(client)
                        .Contains(Search.Trim(), StringComparison.OrdinalIgnoreCase)
                )
            )
            .ToList();

    public IEnumerable<CrmClientView> FilteredConversationClients =>
        Clients
            .Where(client =>
                string.IsNullOrWhiteSpace(ConversationSearch)
                || Searchable(client)
                    .Contains(ConversationSearch.Trim(), StringComparison.OrdinalIgnoreCase)
            )
            .OrderBy(client => client.Name);

    public async Task LoadAsync()
    {
        Ui.Set("CRM", "conversations · calendars · contacts · opportunities · users");
        Loading = true;
        try
        {
            var canManageClientsTask = Permissions.HasAsync(Permission.CrmOpportunityWrite);
            var canRunAutomationsTask = Permissions.HasAsync(Permission.JobsRun);
            var canManageAutomationCatalogTask = Permissions.HasAsync(Permission.CrmAutomationManage);
            var canAssignAutomationTask = Permissions.HasAsync(Permission.CrmAutomationAssign);
            var canPostNotesTask = Permissions.HasAsync(Permission.CrmOpportunityWrite);
            var canUploadArtifactsTask = Permissions.HasAsync(Permission.CrmOpportunityWrite);
            var canManageCalendarsTask = Permissions.HasAsync(Permission.DataWrite);
            var canSendCommsTask = Permissions.HasAsync(Permission.CrmCommsSend);
            var canManageMembersTask = Permissions.HasAsync(Permission.MembersManage);

            var clientsTask = Svc.ListCrmClientsAsync(ProjectId);
            var chainsTask = Svc.ListJobChainsAsync(ProjectId);
            var automationsTask = Svc.ListCrmAutomationRulesAsync(ProjectId);
            var appointmentsTask = Svc.ListCrmAppointmentsAsync(ProjectId);
            var calendarsTask = Svc.ListCrmCalendarsAsync(ProjectId);
            await Task.WhenAll(
                canManageClientsTask,
                canRunAutomationsTask,
                canManageAutomationCatalogTask,
                canAssignAutomationTask,
                canPostNotesTask,
                canUploadArtifactsTask,
                canManageCalendarsTask,
                canSendCommsTask,
                canManageMembersTask,
                clientsTask,
                chainsTask,
                automationsTask,
                appointmentsTask,
                calendarsTask
            );
            _canManageClients = await canManageClientsTask;
            _canRunAutomations = await canRunAutomationsTask;
            _canManageAutomationCatalog = await canManageAutomationCatalogTask;
            _canAssignAutomationChains = await canAssignAutomationTask;
            _canPostClientNotes = await canPostNotesTask;
            _canUploadArtifacts = await canUploadArtifactsTask;
            _canManageCalendars = await canManageCalendarsTask;
            _canSendCommunications = await canSendCommsTask;
            _canManageMembers = await canManageMembersTask;
            Clients = (await clientsTask).ToList();
            Chains = await chainsTask;
            AutomationRules = await automationsTask;
            Appointments = await appointmentsTask;
            Calendars = await calendarsTask;
            CrmUsers = _canManageMembers
                ? await Svc.ListCrmUsersAsync(ProjectId)
                : Array.Empty<CrmUserView>();
        }
        catch (Exception ex)
        {
            Message = ex.Message;
        }
        finally
        {
            Loading = false;
        }
    }

    public string CrmSectionTitle() =>
        CrmSection switch
        {
            "conversations" => "Conversations",
            "calendars" => "Calendars",
            "contacts" => "Contacts",
            "opportunities" => "Opportunities",
            "automations" => "Automations",
            "users" => "Users",
            _ => "CRM settings",
        };

    public string CrmSectionDescription() =>
        CrmSection switch
        {
            "conversations" =>
                "Keep customer email, SMS, and internal notes in one shared timeline.",
            "calendars" => "Manage customer appointments and connected calendars.",
            "contacts" => "Manage customer contact details and relationship context.",
            "opportunities" => "Move customer opportunities through the full lifecycle pipeline.",
            "automations" => "Connect customer lifecycle events to durable job-chain workflows.",
            "users" => "Create CRM users and control who can access each contact.",
            _ => "Connect external lead forms without exposing the rest of your CRM.",
        };

    public void ToggleCrmNavigation() => CrmNavOpen = !CrmNavOpen;

    public async Task OpenCrmSection(string section)
    {
        if (CrmSection == "conversations" && section != "conversations")
            CloseClient();
        CrmSection = section;
        CrmNavOpen = false;
        if (section == "conversations" && Selected is null && Clients.Count > 0)
            await OpenConversation(Clients.OrderBy(client => client.Name).First());
        if (section == "settings" && IngestionSettings is null)
            await LoadIngestionSettings();
    }

    public async Task OpenConversation(CrmClientView client)
    {
        Selected = client;
        DetailTab = "comms";
        ComposeChannel = "Note";
        ComposeSubject = ComposeBody = "";
        CommsError = null;
        Communications = Array.Empty<CrmCommunicationView>();
        await LoadCommunications();
    }

    public void OpenAppointmentEditor() =>
        OpenAppointmentEditor(DateOnly.FromDateTime(DateTime.Now));

    public void OpenAppointmentEditor(DateOnly day)
    {
        var now = DateTimeOffset.Now.ToWorkspaceTime();
        var hour = day == DateOnly.FromDateTime(DateTime.Now) ? Math.Min(22, now.Hour + 1) : 9;
        var starts = new DateTimeOffset(
            day.Year,
            day.Month,
            day.Day,
            hour,
            0,
            0,
            TimeSpan.FromHours(10)
        );
        EditAppointmentId = null;
        AppointmentTitle = "";
        AppointmentClientId = "";
        AppointmentCalendarId = OpenCalendarId is { } id && id != Guid.Empty ? id.ToString() : "";
        AppointmentStartsAt = starts.ToString("yyyy-MM-ddTHH:mm");
        AppointmentEndsAt = starts.AddHours(1).ToString("yyyy-MM-ddTHH:mm");
        AppointmentLocation = AppointmentNotes = null;
        AppointmentError = null;
        ShowAppointmentEditor = true;
    }

    public void OpenAppointmentEditor(CrmAppointmentView appointment)
    {
        EditAppointmentId = appointment.Id;
        AppointmentTitle = appointment.Title;
        AppointmentClientId = appointment.ClientId?.ToString() ?? "";
        AppointmentCalendarId = appointment.CalendarId?.ToString() ?? "";
        AppointmentStartsAt = appointment.StartsAt.ToWorkspaceTime().ToString("yyyy-MM-ddTHH:mm");
        AppointmentEndsAt = appointment.EndsAt.ToWorkspaceTime().ToString("yyyy-MM-ddTHH:mm");
        AppointmentLocation = appointment.Location;
        AppointmentNotes = appointment.Notes;
        AppointmentError = null;
        ShowAppointmentEditor = true;
    }

    public void CloseAppointmentEditor() => ShowAppointmentEditor = false;

    public void OnAppointmentStartsChanged(ChangeEventArgs args) =>
        AppointmentStartsAt = args.Value?.ToString() ?? "";

    public void OnAppointmentEndsChanged(ChangeEventArgs args) =>
        AppointmentEndsAt = args.Value?.ToString() ?? "";

    public async Task SaveAppointment()
    {
        AppointmentError = null;
        if (string.IsNullOrWhiteSpace(AppointmentTitle))
        {
            AppointmentError = "Appointment title is required.";
            return;
        }
        if (
            !TryAppointmentTime(AppointmentStartsAt, out var starts)
            || !TryAppointmentTime(AppointmentEndsAt, out var ends)
        )
        {
            AppointmentError = "Choose a valid start and end time.";
            return;
        }
        if (ends <= starts)
        {
            AppointmentError = "End time must be after the start time.";
            return;
        }

        SavingAppointment = true;
        try
        {
            var clientId = Guid.TryParse(AppointmentClientId, out var parsedClientId)
                ? parsedClientId
                : (Guid?)null;
            var calendarId = Guid.TryParse(AppointmentCalendarId, out var parsedCalendarId)
                ? parsedCalendarId
                : (Guid?)null;
            await Svc.CreateCrmAppointmentAsync(
                new CreateCrmAppointmentCommand(
                    ProjectId,
                    calendarId,
                    clientId,
                    AppointmentTitle,
                    starts,
                    ends,
                    AppointmentLocation,
                    AppointmentNotes,
                    EditAppointmentId
                )
            );
            await RefreshCalendarData();
            ShowAppointmentEditor = false;
        }
        catch (Exception ex)
        {
            AppointmentError = ex.Message;
        }
        finally
        {
            SavingAppointment = false;
        }
    }

    public async Task DeleteAppointment()
    {
        if (
            EditAppointmentId is not { } id
            || !await Js.InvokeAsync<bool>("confirm", "Delete this event?")
        )
            return;
        SavingAppointment = true;
        try
        {
            await Svc.DeleteCrmAppointmentAsync(id);
            await RefreshCalendarData();
            ShowAppointmentEditor = false;
        }
        catch (Exception ex)
        {
            AppointmentError = ex.Message;
        }
        finally
        {
            SavingAppointment = false;
        }
    }

    public void OpenCalendar(Guid id)
    {
        OpenCalendarId = id;
        GoToCurrentMonth();
    }

    public void CloseCalendar() => OpenCalendarId = null;

    public string OpenCalendarName =>
        OpenCalendarId == Guid.Empty
            ? "General"
            : Calendars.FirstOrDefault(x => x.Id == OpenCalendarId)?.Name ?? "Calendar";
    public string OpenCalendarColor =>
        OpenCalendarId == Guid.Empty
            ? "#4f7cff"
            : Calendars.FirstOrDefault(x => x.Id == OpenCalendarId)?.Color ?? "#4f7cff";
    public static readonly string[] CalendarWeekdays =
    [
        "Sun",
        "Mon",
        "Tue",
        "Wed",
        "Thu",
        "Fri",
        "Sat",
    ];
    public IReadOnlyList<DateOnly> CalendarDays
    {
        get
        {
            var first = new DateOnly(CalendarMonth.Year, CalendarMonth.Month, 1);
            var start = first.AddDays(-(int)first.DayOfWeek);
            return Enumerable.Range(0, 42).Select(start.AddDays).ToArray();
        }
    }

    public IReadOnlyList<CrmAppointmentView> AppointmentsFor(DateOnly day) =>
        Appointments
            .Where(x =>
                (
                    OpenCalendarId == Guid.Empty
                        ? x.CalendarId is null
                        : x.CalendarId == OpenCalendarId
                )
                && DateOnly.FromDateTime(x.StartsAt.ToWorkspaceTime().DateTime) == day
            )
            .OrderBy(x => x.StartsAt)
            .ToArray();

    public void ChangeCalendarMonth(int months) => CalendarMonth = CalendarMonth.AddMonths(months);

    public void GoToCurrentMonth() =>
        CalendarMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);

    public void OpenCalendarEditor(CrmCalendarView? calendar = null)
    {
        EditCalendarId = calendar?.Id;
        CalendarName = calendar?.Name ?? "";
        CalendarColor = calendar?.Color ?? "#4f7cff";
        CalendarError = null;
        ShowCalendarEditor = true;
    }

    public void CloseCalendarEditor() => ShowCalendarEditor = false;

    public async Task SaveCalendar()
    {
        SavingCalendar = true;
        CalendarError = null;
        try
        {
            await Svc.SaveCrmCalendarAsync(
                new SaveCrmCalendarCommand(ProjectId, CalendarName, CalendarColor, EditCalendarId)
            );
            await RefreshCalendarData();
            ShowCalendarEditor = false;
        }
        catch (Exception ex)
        {
            CalendarError = ex.Message;
        }
        finally
        {
            SavingCalendar = false;
        }
    }

    public async Task DeleteCalendar(Guid id)
    {
        if (
            !await Js.InvokeAsync<bool>(
                "confirm",
                "Delete this calendar? Its events will move to General."
            )
        )
            return;
        try
        {
            await Svc.DeleteCrmCalendarAsync(id);
            await RefreshCalendarData();
            if (OpenCalendarId == id)
                OpenCalendarId = null;
        }
        catch (Exception ex)
        {
            Message = ex.Message;
        }
    }

    public async Task RefreshCalendarData()
    {
        var calendarsTask = Svc.ListCrmCalendarsAsync(ProjectId);
        var appointmentsTask = Svc.ListCrmAppointmentsAsync(ProjectId);
        await Task.WhenAll(calendarsTask, appointmentsTask);
        Calendars = await calendarsTask;
        Appointments = await appointmentsTask;
    }

    public static bool TryAppointmentTime(string value, out DateTimeOffset result)
    {
        result = default;
        if (
            !DateTime.TryParse(
                value,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,
                out var local
            )
        )
            return false;
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Australia/Brisbane");
        result = new DateTimeOffset(
            DateTime.SpecifyKind(local, DateTimeKind.Unspecified),
            zone.GetUtcOffset(local)
        );
        return true;
    }

    public async Task LoadIngestionSettings()
    {
        LoadingIngestionSettings = true;
        IngestionError = null;
        try
        {
            if (!await CanManageIngestion())
            {
                return;
            }
            IngestionSettings = await IngestionSettingsService.GetAsync(ProjectId);
            if (!IngestionSettings.Any())
            {
                IngestionClientId = Guid.Empty;
                IngestionOrigin = "";
                return;
            }
            if (!IngestionSettings.Any(setting => setting.ClientId == IngestionClientId))
                IngestionClientId = IngestionSettings[0].ClientId;
            else
                IngestionOrigin = SelectedIngestionSetting?.AllowedOrigin ?? "";
        }
        catch (Exception ex)
        {
            IngestionError = ex.Message;
        }
        finally
        {
            LoadingIngestionSettings = false;
        }
    }

    public async Task SaveIngestionOrigin()
    {
        SavingIngestion = true;
        IngestionError = null;
        IngestionMessage = null;
        NewIngestionToken = null;
        try
        {
            if (!await CanManageIngestion())
                return;
            var result = await IngestionSettingsService.SaveOriginAsync(
                ProjectId,
                IngestionClientId,
                IngestionOrigin
            );
            await LoadIngestionSettings();
            IngestionOrigin = result.AllowedOrigin;
            IngestionMessage = "Allowed origin saved.";
        }
        catch (Exception ex)
        {
            IngestionError = ex.Message;
        }
        finally
        {
            SavingIngestion = false;
        }
    }

    public async Task RotateIngestionToken()
    {
        SavingIngestion = true;
        IngestionError = null;
        IngestionMessage = null;
        NewIngestionToken = null;
        try
        {
            if (!await CanManageIngestion())
                return;
            var result = await IngestionSettingsService.RotateAsync(
                ProjectId,
                IngestionClientId,
                IngestionOrigin
            );
            await LoadIngestionSettings();
            NewIngestionToken = result?.Token;
            IngestionMessage = "CRM lead ingestion is enabled.";
        }
        catch (Exception ex)
        {
            IngestionError = ex.Message;
        }
        finally
        {
            SavingIngestion = false;
        }
    }

    public async Task DisableIngestion()
    {
        SavingIngestion = true;
        IngestionError = null;
        IngestionMessage = null;
        NewIngestionToken = null;
        try
        {
            if (!await CanManageIngestion())
                return;
            await IngestionSettingsService.DisableAsync(ProjectId, IngestionClientId);
            await LoadIngestionSettings();
            IngestionMessage = "CRM lead ingestion disabled.";
        }
        catch (Exception ex)
        {
            IngestionError = ex.Message;
        }
        finally
        {
            SavingIngestion = false;
        }
    }

    public async Task CopyAsync(string value)
    {
        await Js.InvokeVoidAsync("navigator.clipboard.writeText", value);
        IngestionMessage = "Copied to clipboard.";
    }

    public async Task<bool> CanManageIngestion()
    {
        if (await Permissions.HasAsync(Permission.SettingsManage))
            return true;
        IngestionError = "The settings.manage permission is required to configure CRM ingestion.";
        return false;
    }

    public string IngestionExample() =>
        $$"""
            fetch("{{IngestionEndpoint}}", {
              method: "POST",
              headers: {
                "Content-Type": "application/json",
                "{{CrmIngestionSettingsService.TokenHeader}}": "{{(NewIngestionToken
                ?? SelectedIngestionSetting?.TokenPrefix
                ?? "YOUR_TOKEN")}}"
              },
              body: JSON.stringify({
                address: "123 Example Street, Brisbane QLD 4000"
              })
            });
            """;

    public void NewPrimary()
    {
        if (CrmSection == "automations")
            NewAutomation();
        else
            NewClient();
    }

    public async Task CreateCrmUser()
    {
            CrmUserError = null;
            NewCrmUserJoinCode = null;
            NewCrmUserJoinLink = null;
            NewCrmUserJoinMessage = null;
        if (!CanManageMembers)
        {
            CrmUserError =
                "The members.manage permission is required to invite users to CRM access.";
            return;
        }
        if (string.IsNullOrWhiteSpace(NewCrmUserEmail) || !NewCrmUserEmail.Contains('@'))
        {
            CrmUserError = "Enter a valid email.";
            return;
        }

        CreatingCrmUser = true;
        try
        {
            var email = NewCrmUserEmail.Trim();
            var name = string.IsNullOrWhiteSpace(NewCrmUserName)
                ? null
                : NewCrmUserName.Trim();
            var assignedClientId = Guid.TryParse(NewCrmUserClientId, out var parsedClientId)
                ? parsedClientId
                : (Guid?)null;
            if (assignedClientId == Guid.Empty)
                assignedClientId = null;
            var result = await Svc.CreateCrmUserAsync(new CreateCrmUserCommand(
                ProjectId,
                email,
                name,
                assignedClientId
            ));
            CrmUsers = await Svc.ListCrmUsersAsync(ProjectId);
            NewCrmUserJoinCode = result.JoinCode;
            NewCrmUserJoinLink = $"/crm/onboarding?code={result.JoinCode}";
            var notes = new List<string>(capacity: 2);
            if (result.AssignedClientId is { } assigned && assigned != Guid.Empty)
            {
                var assignedClient = Clients.FirstOrDefault(client => client.Id == assigned);
                notes.Add(string.IsNullOrWhiteSpace(assignedClient?.Name)
                    ? "Assigned to selected contact."
                    : $"Assigned to {assignedClient.Name}.");
            }
            if (result.EmailSent)
            {
                notes.Add($"An onboarding email was sent to {email}.");
            }
            else if (result.EmailProviderAvailable)
            {
                notes.Add($"Email could not be sent: {result.EmailError ?? "unexpected error"}");
            }
            else
            {
                notes.Add("No email provider is configured. Share the onboarding link manually.");
            }
            NewCrmUserJoinMessage = string.Join(" ", notes);
            NewCrmUserEmail = "";
            NewCrmUserName = "";
            NewCrmUserClientId = "";
            Message = $"CRM user added for {email}.";
            CrmUserError = null;
        }
        catch (Exception ex)
        {
            CrmUserError = ex.Message;
        }
        finally
        {
            CreatingCrmUser = false;
        }
    }

    public async Task DeleteCrmUser(Guid crmUserId)
    {
        if (!CanManageMembers)
            return;

        var user = CrmUsers.FirstOrDefault(item => item.Id == crmUserId);
        if (user is null)
            return;

        if (!await Js.InvokeAsync<bool>("confirm", $"Remove {user.Email} from CRM users?"))
            return;

        try
        {
            await Svc.DeleteCrmUserAsync(ProjectId, crmUserId);
            CrmUsers = CrmUsers.Where(item => item.Id != crmUserId).ToArray();
            if (Selected is not null && SelectedClientUserIds.Remove(crmUserId))
                ClientUserAssignmentMessage = $"{user.Email} was unassigned from this contact.";
            Message = $"{user.Email} removed from CRM users.";
        }
        catch (Exception ex)
        {
            CrmUserError = ex.Message;
        }
    }

    public void NewClient()
    {
        EditId = null;
        EditName = "";
        EditCompany = EditEmail = EditPhone = EditNotes = null;
        EditStage = CustomerLifecycleStage.Lead;
        FormError = null;
        ShowEditor = true;
    }

    public void EditClient(CrmClientView client)
    {
        EditId = client.Id;
        EditName = client.Name;
        EditCompany = client.Company;
        EditEmail = client.Email;
        EditPhone = client.Phone;
        EditStage = ParseStage(client.LifecycleStage);
        EditNotes = client.Notes;
        FormError = null;
        Selected = null;
        ClientRuns = Array.Empty<CrmChainRunView>();
        ConfirmDelete = false;
        ShowEditor = true;
    }

    public void CloseEditor() => ShowEditor = false;

    public void NewAutomation()
    {
        EditAutomationId = null;
        AutomationName = "";
        AutomationEvent = CrmAutomationEventType.StageEntered;
        AutomationStage = null;
        AutomationChainId = null;
        AutomationEnabled = true;
        AutomationError = null;
        ShowAutomationEditor = true;
    }

    public void EditAutomation(CrmAutomationRuleView rule)
    {
        EditAutomationId = rule.Id;
        AutomationName = rule.Name;
        AutomationEvent = Enum.TryParse<CrmAutomationEventType>(rule.EventType, out var eventType)
            ? eventType
            : CrmAutomationEventType.StageEntered;
        AutomationStage =
            rule.LifecycleStage is not null
            && Enum.TryParse<CustomerLifecycleStage>(rule.LifecycleStage, out var stage)
                ? stage
                : null;
        AutomationChainId = rule.ChainId;
        AutomationEnabled = rule.Enabled;
        AutomationError = null;
        ShowAutomationEditor = true;
    }

    public void CloseAutomationEditor()
    {
        ShowAutomationEditor = false;
        AutomationError = null;
    }

    public async Task SaveAutomation()
    {
        AutomationError = null;
        if (string.IsNullOrWhiteSpace(AutomationName))
        {
            AutomationError = "Enter an automation name.";
            return;
        }
        if (AutomationChainId is null)
        {
            AutomationError = "Select a job chain.";
            return;
        }

        SavingAutomation = true;
        try
        {
            await Svc.SaveCrmAutomationRuleAsync(
                new SaveCrmAutomationRuleCommand(
                    ProjectId,
                    AutomationName,
                    AutomationEvent,
                    AutomationStage,
                    AutomationChainId.Value,
                    AutomationEnabled,
                    EditAutomationId
                )
            );
            await RefreshAutomationRules();
            ShowAutomationEditor = false;
            Message = EditAutomationId is null ? "Automation created." : "Automation updated.";
        }
        catch (Exception ex)
        {
            AutomationError = ex.Message;
        }
        finally
        {
            SavingAutomation = false;
        }
    }

    public async Task ToggleAutomation(CrmAutomationRuleView rule)
    {
        try
        {
            await Svc.SetCrmAutomationEnabledAsync(rule.Id, !rule.Enabled);
            await RefreshAutomationRules();
            Message = $"{rule.Name} {(rule.Enabled ? "paused" : "enabled")}.";
        }
        catch (Exception ex)
        {
            Message = ex.Message;
        }
    }

    public async Task DeleteAutomation(CrmAutomationRuleView rule)
    {
        try
        {
            await Svc.DeleteCrmAutomationRuleAsync(rule.Id);
            ConfirmAutomationDeleteId = null;
            await RefreshAutomationRules();
            Message = $"{rule.Name} deleted.";
        }
        catch (Exception ex)
        {
            Message = ex.Message;
        }
    }

    public async Task SaveClient()
    {
        FormError = null;
        Saving = true;
        try
        {
            var saved = await Svc.SaveCrmClientAsync(
                new SaveCrmClientCommand(
                    ProjectId,
                    EditName,
                    EditCompany,
                    EditEmail,
                    EditPhone,
                    EditStage,
                    EditNotes,
                    EditId
                )
            );
            await RefreshClients();
            if (Selected?.Id == saved.Id)
                Selected = saved;
            ShowEditor = false;
            Message = EditId is null ? "Client added." : "Client updated.";
        }
        catch (Exception ex)
        {
            FormError = ex.Message;
        }
        finally
        {
            Saving = false;
        }
    }

    public async Task OpenClient(CrmClientView client)
    {
        Selected = client;
        SelectedChainId = null;
        SelectedClientChainIds = new HashSet<Guid>();
        SelectedClientUserIds = new HashSet<Guid>();
        ConfirmDelete = false;
        DetailTab = "overview";
        ComposeChannel = "Note";
        ComposeSubject = ComposeBody = "";
        CommsError = null;
        ArtifactError = null;
        ArtifactSearch = "";
        ArtifactSourceFilter = "all";
        ConfirmArtifactRemoveId = null;
        await Task.WhenAll(
            LoadRuns(),
            LoadCommunications(),
            LoadArtifacts(),
            LoadClientChainAssignments(),
            LoadClientUserAssignments()
        );
    }

    public void CloseClient()
    {
        Selected = null;
        ClientRuns = Array.Empty<CrmChainRunView>();
        Communications = Array.Empty<CrmCommunicationView>();
        ClientArtifacts = Array.Empty<CrmClientArtifactView>();
        SelectedClientChainIds = new HashSet<Guid>();
        ClientChainAssignmentMessage = null;
        LoadingClientChainAssignments = false;
        SavingClientChainAssignments = false;
        SelectedClientUserIds = new HashSet<Guid>();
        ClientUserAssignmentMessage = null;
        LoadingClientUserAssignments = false;
        SavingClientUserAssignments = false;
        NotesMetadataOpen = false;
        NotesMetadataJson = null;
    }

    public bool HasNotesMetadata => !string.IsNullOrWhiteSpace(ExtractNotesMetadata(Selected?.Notes));

    public void OpenNotesMetadata()
    {
        NotesMetadataJson = ExtractNotesMetadata(Selected?.Notes);
        NotesMetadataOpen = !string.IsNullOrWhiteSpace(NotesMetadataJson);
    }

    public void CloseNotesMetadata()
    {
        NotesMetadataOpen = false;
        NotesMetadataJson = null;
    }

    public static string? ExtractNotesMetadata(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes))
            return null;

        var start = notes.IndexOfAny(['{', '[']);
        if (start < 0)
            return null;

        char open = notes[start];
        char close = open == '{' ? '}' : ']';
        int depth = 1;
        bool inString = false;
        bool escape = false;
        int i = start + 1;

        while (i < notes.Length && depth > 0)
        {
            char c = notes[i];
            if (escape)
            {
                escape = false;
            }
            else if (c == '\\')
            {
                escape = true;
            }
            else if (c == '"')
            {
                inString = !inString;
            }
            else if (!inString)
            {
                if (c == open)
                    depth++;
                else if (c == close)
                    depth--;
            }
            i++;
        }

        if (depth != 0)
            return null;

        var json = notes[start..i];
        try
        {
            using var doc = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });
        }
        catch
        {
            return null;
        }
    }

    public async Task CopyNotesMetadataAsync()
    {
        if (!string.IsNullOrWhiteSpace(NotesMetadataJson))
            await Js.InvokeVoidAsync("navigator.clipboard.writeText", NotesMetadataJson);
    }

    public async Task MoveClient(CrmClientView client, ChangeEventArgs args)
    {
        if (!Enum.TryParse<CustomerLifecycleStage>(args.Value?.ToString(), out var stage))
            return;
        try
        {
            var moved = await Svc.MoveCrmClientAsync(client.Id, stage);
            await RefreshClients();
            if (Selected?.Id == moved.Id)
                Selected = moved;
            Message = $"{moved.Name} moved to {StageLabel(stage)}.";
        }
        catch (Exception ex)
        {
            Message = ex.Message;
        }
    }

    public async Task RunAutomation()
    {
        if (Selected is null || SelectedChainId is null)
            return;
        Running = true;
        Message = null;
        try
        {
            var run = await Svc.RunCrmClientAutomationAsync(Selected.Id, SelectedChainId.Value);
            Message = $"{run.ChainName} finished with status {run.Status}.";
            await Task.WhenAll(LoadRuns(), LoadArtifacts());
        }
        catch (Exception ex)
        {
            Message = ex.Message;
        }
        finally
        {
            Running = false;
        }
    }

    public bool IsChainAssigned(Guid chainId)
    {
        return SelectedClientChainIds.Contains(chainId);
    }

    public bool IsUserAssigned(Guid crmUserId)
    {
        return SelectedClientUserIds.Contains(crmUserId);
    }

    public void ToggleClientChainAssignment(Guid chainId, bool assigned)
    {
        if (assigned)
            SelectedClientChainIds.Add(chainId);
        else
            SelectedClientChainIds.Remove(chainId);
    }

    public async Task SaveClientChainAssignments()
    {
        if (Selected is null)
            return;
        if (!_canAssignAutomationChains && !_canManageAutomationCatalog)
        {
            ClientChainAssignmentMessage =
                "A CRM automation-assignment permission is required to edit automation assignments.";
            return;
        }

        SavingClientChainAssignments = true;
        ClientChainAssignmentMessage = null;
        try
        {
            var chainIds = SelectedClientChainIds.OrderBy(chainId => chainId).ToList();
            SelectedClientChainIds = (await Svc.SetCrmClientAssignedJobChainIdsAsync(
                Selected.Id,
                ProjectId,
                chainIds
            )).ToHashSet();
            ClientChainAssignmentMessage = "Automation assignment settings saved.";
        }
        catch (Exception ex)
        {
            ClientChainAssignmentMessage = ex.Message;
        }
        finally
        {
            SavingClientChainAssignments = false;
        }
    }

    public void ToggleClientUserAssignment(Guid crmUserId, bool assigned)
    {
        if (assigned)
            SelectedClientUserIds.Add(crmUserId);
        else
            SelectedClientUserIds.Remove(crmUserId);
    }

    public async Task SaveClientUserAssignments()
    {
        if (Selected is null)
            return;
        if (!_canManageMembers)
        {
            ClientUserAssignmentMessage =
                "The members.manage permission is required to edit CRM user assignments.";
            return;
        }

        SavingClientUserAssignments = true;
        ClientUserAssignmentMessage = null;
        try
        {
            var crmUserIds = SelectedClientUserIds.OrderBy(userId => userId).ToList();
            SelectedClientUserIds = (await Svc.SetCrmClientAssignedUserIdsAsync(
                ProjectId,
                Selected.Id,
                crmUserIds
            )).ToHashSet();
            ClientUserAssignmentMessage = "CRM user assignments saved.";
        }
        catch (Exception ex)
        {
            ClientUserAssignmentMessage = ex.Message;
        }
        finally
        {
            SavingClientUserAssignments = false;
        }
    }

    private async Task LoadClientChainAssignments()
    {
        if (Selected is null)
            return;
        if (!_canAssignAutomationChains && !_canManageAutomationCatalog)
            return;

        LoadingClientChainAssignments = true;
        ClientChainAssignmentMessage = null;
        try
        {
            SelectedClientChainIds = (await Svc.ListCrmClientAssignedJobChainIdsAsync(
                Selected.Id,
                ProjectId
            )).ToHashSet();
        }
        catch (Exception ex)
        {
            ClientChainAssignmentMessage = ex.Message;
        }
        finally
        {
            LoadingClientChainAssignments = false;
        }
    }

    private async Task LoadClientUserAssignments()
    {
        if (Selected is null)
            return;
        if (!_canManageMembers)
            return;

        LoadingClientUserAssignments = true;
        ClientUserAssignmentMessage = null;
        try
        {
            SelectedClientUserIds = (await Svc.ListCrmClientAssignedUserIdsAsync(
                Selected.Id,
                ProjectId
            )).ToHashSet();
        }
        catch (Exception ex)
        {
            ClientUserAssignmentMessage = ex.Message;
        }
        finally
        {
            LoadingClientUserAssignments = false;
        }
    }

    public async Task DeleteClient()
    {
        if (Selected is null)
            return;
        try
        {
            var name = Selected.Name;
            await Svc.DeleteCrmClientAsync(Selected.Id);
            CloseClient();
            await RefreshClients();
            Message = $"{name} deleted.";
        }
        catch (Exception ex)
        {
            Message = ex.Message;
        }
    }

    public async Task LoadRuns()
    {
        if (Selected is null)
            return;
        LoadingRuns = true;
        try
        {
            ClientRuns = await Svc.ListCrmClientChainRunsAsync(Selected.Id);
        }
        catch (Exception ex)
        {
            Message = ex.Message;
        }
        finally
        {
            LoadingRuns = false;
        }
    }

    public async Task LoadCommunications()
    {
        if (Selected is null)
            return;
        LoadingComms = true;
        try
        {
            var communicationsTask = Svc.ListCrmClientCommunicationsAsync(Selected.Id);
            var capabilitiesTask = Svc.GetCrmCommsCapabilitiesAsync();
            await Task.WhenAll(communicationsTask, capabilitiesTask);
            Communications = await communicationsTask;
            CommsCapabilities = await capabilitiesTask;
        }
        catch (Exception ex)
        {
            CommsError = ex.Message;
        }
        finally
        {
            LoadingComms = false;
        }
    }

    public async Task LoadArtifacts()
    {
        if (Selected is null)
            return;
        LoadingArtifacts = true;
        try
        {
            ClientArtifacts = await Svc.ListCrmClientArtifactsAsync(Selected.Id);
        }
        catch (Exception ex)
        {
            ArtifactError = ex.Message;
        }
        finally
        {
            LoadingArtifacts = false;
        }
    }

    public async Task AttachArtifact(InputFileChangeEventArgs args)
    {
        if (Selected is null)
            return;
        const long maxBytes = 20L * 1024 * 1024;
        UploadingArtifact = true;
        ArtifactError = null;
        try
        {
            var file = args.File;
            if (file.Size > maxBytes)
                throw new InvalidOperationException("Files must be 20 MB or smaller.");
            await using var input = file.OpenReadStream(maxBytes);
            using var buffer = new MemoryStream();
            await input.CopyToAsync(buffer);
            await Svc.AttachCrmClientArtifactAsync(
                Selected.Id,
                file.Name,
                file.ContentType,
                buffer.ToArray()
            );
            await LoadArtifacts();
        }
        catch (Exception ex)
        {
            ArtifactError = ex.Message;
        }
        finally
        {
            UploadingArtifact = false;
        }
    }

    public async Task RemoveArtifact(CrmClientArtifactView artifact)
    {
        ArtifactError = null;
        try
        {
            await Svc.RemoveCrmClientArtifactAsync(artifact.Id);
            ConfirmArtifactRemoveId = null;
            await LoadArtifacts();
        }
        catch (Exception ex)
        {
            ArtifactError = ex.Message;
        }
    }

    public void SelectChannel(string channel)
    {
        if (!ChannelAvailable(channel))
            return;
        ComposeChannel = channel;
        CommsError = null;
    }

    public async Task SubmitCommunication()
    {
        if (Selected is null || !CanSubmitComms)
            return;
        SendingComms = true;
        CommsError = null;
        try
        {
            CrmCommunicationView result;
            if (ComposeChannel == "Note")
            {
                result = await Svc.AddCrmClientNoteAsync(Selected.Id, ComposeBody);
            }
            else
            {
                var channel =
                    ComposeChannel == "Email"
                        ? PlaceContext.Domain.ValueObjects.CrmCommunicationChannel.Email
                        : PlaceContext.Domain.ValueObjects.CrmCommunicationChannel.Sms;
                result = await Svc.SendCrmClientMessageAsync(
                    Selected.Id,
                    channel,
                    ComposeSubject,
                    ComposeBody
                );
            }
            ComposeBody = "";
            ComposeSubject = "";
            await LoadCommunications();
            if (result.Status == "Failed")
                CommsError = result.Error ?? "Message delivery failed.";
        }
        catch (Exception ex)
        {
            CommsError = ex.Message;
        }
        finally
        {
            SendingComms = false;
        }
    }

    public bool CanSubmitComms =>
        !SendingComms
        && !string.IsNullOrWhiteSpace(ComposeBody)
        && (ComposeChannel != "Email" || !string.IsNullOrWhiteSpace(ComposeSubject))
        && ChannelAvailable(ComposeChannel);

    public bool ChannelAvailable(string channel) =>
        channel switch
        {
            "Email" => Selected?.Email is not null && (CommsCapabilities?.EmailEnabled ?? false),
            "Sms" => Selected?.Phone is not null && (CommsCapabilities?.SmsEnabled ?? false),
            _ => true,
        };

    public string ChannelHelp(string channel) =>
        channel switch
        {
            "Email" when Selected?.Email is null => "Add an email address to this client first.",
            "Email" when !(CommsCapabilities?.EmailEnabled ?? false) =>
                "Connect Postmark in Settings → Communications.",
            "Sms" when Selected?.Phone is null => "Add a phone number to this client first.",
            "Sms" when !(CommsCapabilities?.SmsEnabled ?? false) =>
                "Configure Twilio to enable SMS.",
            _ => ChannelLabel(channel),
        };

    public string RecipientHint() =>
        ComposeChannel switch
        {
            "Email" => $"To: {Selected?.Email}",
            "Sms" => $"To: {Selected?.Phone}",
            _ => "Visible to your CRM team",
        };

    public string ComposerPlaceholder() =>
        ComposeChannel switch
        {
            "Email" => $"Write an email to {Selected?.Name}…",
            "Sms" => $"Write an SMS to {Selected?.Name}…",
            _ => "Add context, a follow-up, or the next step…",
        };

    public string SubmitLabel() =>
        ComposeChannel switch
        {
            "Email" => "Send email",
            "Sms" => "Send SMS",
            _ => "Add note",
        };

    public static string ChannelLabel(string channel) => channel == "Sms" ? "SMS" : channel;

    public static string ChannelIconName(string channel) =>
        channel switch
        {
            "Email" => "mail",
            "Sms" => "phone",
            _ => "note",
        };

    public static string FormatBytes(long bytes) =>
        bytes switch
        {
            >= 1024 * 1024 => $"{bytes / 1024d / 1024d:0.#} MB",
            >= 1024 => $"{bytes / 1024d:0.#} KB",
            _ => $"{bytes} B",
        };

    // CRM presents one row per logical client file. Older records remain stored and available
    // to the artifact system, but the client workspace always resolves the newest version.
    public IReadOnlyList<CrmClientArtifactView> LatestClientArtifacts =>
        ClientArtifacts
            .GroupBy(item => item.Title.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(item => item.CreatedAt).First())
            .OrderByDescending(item => item.CreatedAt)
            .ToArray();

    public IEnumerable<CrmClientArtifactView> FilteredClientArtifacts =>
        LatestClientArtifacts.Where(item =>
            (
                ArtifactSourceFilter == "all"
                || item.Source.Equals(ArtifactSourceFilter, StringComparison.OrdinalIgnoreCase)
            )
            && (
                string.IsNullOrWhiteSpace(ArtifactSearch)
                || item.Title.Contains(ArtifactSearch.Trim(), StringComparison.OrdinalIgnoreCase)
                || ArtifactTypeLabel(item.ContentType)
                    .Contains(ArtifactSearch.Trim(), StringComparison.OrdinalIgnoreCase)
            )
        );

    public void OnArtifactSearch(ChangeEventArgs args) =>
        ArtifactSearch = args.Value?.ToString() ?? "";

    public void ClearArtifactFilters()
    {
        ArtifactSearch = "";
        ArtifactSourceFilter = "all";
    }

    public int ArtifactSourceCount(string source) =>
        LatestClientArtifacts.Count(item =>
            item.Source.Equals(source, StringComparison.OrdinalIgnoreCase)
        );

    public static string ArtifactExtension(string title)
    {
        var extension = Path.GetExtension(title).TrimStart('.');
        return string.IsNullOrWhiteSpace(extension)
            ? "FILE"
            : extension[..Math.Min(4, extension.Length)].ToUpperInvariant();
    }

    public static string ArtifactTypeClass(string contentType)
    {
        if (contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            return "type-image";
        if (contentType.Contains("pdf", StringComparison.OrdinalIgnoreCase))
            return "type-pdf";
        if (
            contentType.Contains("json", StringComparison.OrdinalIgnoreCase)
            || contentType.Contains("csv", StringComparison.OrdinalIgnoreCase)
        )
            return "type-data";
        return "type-document";
    }

    public static string ArtifactTypeLabel(string contentType)
    {
        if (contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            return "Image";
        if (contentType.Contains("pdf", StringComparison.OrdinalIgnoreCase))
            return "PDF";
        if (contentType.Contains("json", StringComparison.OrdinalIgnoreCase))
            return "JSON data";
        if (contentType.Contains("csv", StringComparison.OrdinalIgnoreCase))
            return "CSV data";
        if (contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase))
            return "Text";
        if (contentType.Contains("spreadsheet", StringComparison.OrdinalIgnoreCase))
            return "Spreadsheet";
        if (contentType.Contains("word", StringComparison.OrdinalIgnoreCase))
            return "Document";
        return "File";
    }

    public static string RelativeArtifactDate(DateTimeOffset value)
    {
        var local = value.ToWorkspaceTime();
        var today = DateTimeOffset.Now.Date;
        if (local.Date == today)
            return $"Today, {local:HH:mm}";
        if (local.Date == today.AddDays(-1))
            return $"Yesterday, {local:HH:mm}";
        return local.ToString("dd MMM yyyy");
    }

    public async Task RefreshClients() => Clients = await Svc.ListCrmClientsAsync(ProjectId);

    public async Task RefreshAutomationRules() =>
        AutomationRules = await Svc.ListCrmAutomationRulesAsync(ProjectId);

    public string SelectedAutomationChainName =>
        Chains.FirstOrDefault(chain => chain.Id == AutomationChainId)?.Name ?? "Choose a chain";

    public static string AutomationEventLabel(string value) =>
        Enum.TryParse<CrmAutomationEventType>(value, out var eventType)
            ? AutomationEventLabel(eventType)
            : value;

    public static string AutomationEventLabel(CrmAutomationEventType eventType) =>
        eventType switch
        {
            CrmAutomationEventType.ClientCreated => "Client created",
            CrmAutomationEventType.ClientUpdated => "Client updated",
            CrmAutomationEventType.StageEntered => "Stage entered",
            CrmAutomationEventType.NoteAdded => "Note added",
            CrmAutomationEventType.ArtifactAttached => "Artifact attached",
            CrmAutomationEventType.CommunicationSent => "Communication sent",
            CrmAutomationEventType.IngestionReceived => "Ingestion received",
            _ => eventType.ToString(),
        };

    public static string AutomationEventDescription(CrmAutomationEventType eventType) =>
        eventType switch
        {
            CrmAutomationEventType.ClientCreated =>
                "Runs when a new client is added to this project.",
            CrmAutomationEventType.ClientUpdated =>
                "Runs when client contact details or notes are edited.",
            CrmAutomationEventType.StageEntered =>
                "Runs when a client moves into a lifecycle stage.",
            CrmAutomationEventType.NoteAdded => "Runs when an internal note is added in Comms.",
            CrmAutomationEventType.ArtifactAttached =>
                "Runs when a file is attached directly to the client.",
            CrmAutomationEventType.CommunicationSent =>
                "Runs after an email or SMS is sent successfully.",
            CrmAutomationEventType.IngestionReceived =>
                "Runs when this project's CRM ingestion endpoint receives any JSON payload.",
            _ => "",
        };

    public static string AutomationEventIconName(string value) =>
        Enum.TryParse<CrmAutomationEventType>(value, out var eventType)
            ? eventType switch
            {
                CrmAutomationEventType.ClientCreated => "message",
                CrmAutomationEventType.ClientUpdated => "refresh",
                CrmAutomationEventType.StageEntered => "arrow-right",
                CrmAutomationEventType.NoteAdded => "note",
                CrmAutomationEventType.ArtifactAttached => "upload",
                CrmAutomationEventType.CommunicationSent => "mail",
                CrmAutomationEventType.IngestionReceived => "arrow-right",
                _ => "workflow",
            }
            : "workflow";

    public static string AutomationStageLabel(string? value) =>
        value is not null && Enum.TryParse<CustomerLifecycleStage>(value, out var stage)
            ? StageLabel(stage)
            : "Any lifecycle stage";

    public int Count(CustomerLifecycleStage stage) =>
        Clients.Count(c => ParseStage(c.LifecycleStage) == stage);

    public void ToggleStageFilter(CustomerLifecycleStage stage) =>
        StageFilter = StageFilter == stage ? null : stage;

    public static CustomerLifecycleStage ParseStage(string value) =>
        Enum.TryParse<CustomerLifecycleStage>(value, out var stage)
            ? stage
            : CustomerLifecycleStage.Lead;

    public static string StageLabel(CustomerLifecycleStage stage) =>
        stage switch
        {
            CustomerLifecycleStage.AtRisk => "At risk",
            _ => stage.ToString(),
        };

    public static string StageDescription(CustomerLifecycleStage stage) =>
        stage switch
        {
            CustomerLifecycleStage.Lead => "New prospects",
            CustomerLifecycleStage.Qualified => "Ready to engage",
            CustomerLifecycleStage.Onboarding => "Getting started",
            CustomerLifecycleStage.Active => "Current customers",
            CustomerLifecycleStage.AtRisk => "Needs attention",
            CustomerLifecycleStage.Churned => "Closed accounts",
            _ => "",
        };

    public static string StageClass(CustomerLifecycleStage stage) =>
        $"stage-{stage.ToString().ToLowerInvariant()}";

    public static string Searchable(CrmClientView client) =>
        $"{client.Name} {client.Company} {client.Email} {client.Phone}";

    public static string NormalizeEmail(string? email) => email?.Trim() ?? string.Empty;

    public static string Initials(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Concat(parts.Take(2).Select(part => char.ToUpperInvariant(part[0])));
    }
}
