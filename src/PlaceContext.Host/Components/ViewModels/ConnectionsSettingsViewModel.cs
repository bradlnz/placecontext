using Microsoft.AspNetCore.Components;
using PlaceContext.Application;
using PlaceContext.Application.Dtos;
using PlaceContext.Infrastructure.OpenSearch;
using PlaceContext.Infrastructure.ProjectData;

namespace PlaceContext.Host.Components.ViewModels;

/// <summary>
/// Workspace /settings page for per-project data connectivity. Every project uses the shared
/// cluster database by default; an external database or OpenSearch index is configured here and
/// stored as encrypted Vault secrets (the same keys the connection resolvers read at run time).
/// Values are never displayed after save — the Vault stores ciphertext only.
/// </summary>
public sealed class ConnectionsSettingsViewModel(
    IPlaceContextService service,
    PortalUiState ui
) : PageViewModel
{
    public const string PageTitle = "Connections";

    private static readonly string[] ExternalDatabaseKeys =
    [
        ProjectDatabaseConnectionResolver.HostVariable,
        ProjectDatabaseConnectionResolver.PortVariable,
        ProjectDatabaseConnectionResolver.NameVariable,
        ProjectDatabaseConnectionResolver.UsernameVariable,
        ProjectDatabaseConnectionResolver.PasswordVariable,
        ProjectDatabaseConnectionResolver.SslModeVariable,
    ];

    private static readonly string[] ExternalIndexKeys =
    [
        OpenSearchConnectionResolver.EndpointVariable,
        OpenSearchConnectionResolver.UsernameVariable,
        OpenSearchConnectionResolver.PasswordVariable,
        OpenSearchConnectionResolver.IndexVariable,
    ];

    private static readonly string[] SslModes = ["Disable", "Allow", "Prefer", "Require", "Verify-CA", "Verify-Full"];

    public IReadOnlyList<ProjectSummaryView>? Projects { get; private set; }
    public IReadOnlyList<ProjectSecretView> Secrets { get; private set; } = Array.Empty<ProjectSecretView>();
    public Guid? SelectedProjectId { get; set; }
    public bool Loading { get; private set; } = true;
    public bool Busy { get; private set; }
    public string? Message { get; private set; }
    public bool MessageIsError { get; private set; }
    public string? FormError { get; private set; }

    // External database form.
    public string DbHost { get; set; } = "";
    public string DbPort { get; set; } = "";
    public string DbName { get; set; } = "";
    public string DbUser { get; set; } = "";
    public string DbPassword { get; set; } = "";
    public string DbSslMode { get; set; } = "Prefer";

    // External index form.
    public string OsEndpoint { get; set; } = "";
    public string OsUsername { get; set; } = "";
    public string OsPassword { get; set; } = "";
    public string OsIndex { get; set; } = "";

    public string? SelectedProjectName => Projects?.FirstOrDefault(p => p.Id == SelectedProjectId)?.Name;

    public bool HasExternalDatabase =>
        Secrets.Any(s => s.Name == ProjectDatabaseConnectionResolver.HostVariable);

    public bool HasExternalIndex =>
        Secrets.Any(s => s.Name == OpenSearchConnectionResolver.EndpointVariable);

    public IReadOnlyList<string> SslModeOptions => SslModes;

    public async Task LoadAsync()
    {
        ui.Set(PageTitle, "external databases and search indices per project");
        Loading = true;
        Message = null;
        NotifyStateChanged();
        try
        {
            Projects = await service.GetProjectsAsync();
            if (Projects.Count > 0 && SelectedProjectId is null)
                SelectedProjectId = Projects[0].Id;
            await LoadSecretsAsync();
        }
        catch (Exception ex)
        {
            Message = ex.Message;
            MessageIsError = true;
        }
        finally
        {
            Loading = false;
            NotifyStateChanged();
        }
    }

    public async Task LoadSecretsAsync()
    {
        Secrets = SelectedProjectId is { } id
            ? await service.ListProjectSecretsAsync(id)
            : Array.Empty<ProjectSecretView>();
        NotifyStateChanged();
    }

    public async Task ProjectChanged(ChangeEventArgs args)
    {
        SelectedProjectId = Guid.TryParse(args.Value?.ToString(), out var id) ? id : null;
        FormError = null;
        Message = null;
        ClearForms();
        await LoadSecretsAsync();
    }

    public async Task SaveExternalDatabaseAsync()
    {
        FormError = null;
        if (SelectedProjectId is not { } id)
        {
            FormError = "Select a project first.";
            return;
        }
        if (string.IsNullOrWhiteSpace(DbHost))
        {
            FormError = "Host is required.";
            return;
        }
        if (string.IsNullOrWhiteSpace(DbUser))
        {
            FormError = "Username is required.";
            return;
        }
        if (string.IsNullOrEmpty(DbPassword))
        {
            FormError = "Password is required.";
            return;
        }
        var port = string.IsNullOrWhiteSpace(DbPort) ? null : DbPort.Trim();
        if (port is not null && !int.TryParse(port, out _))
        {
            FormError = "Port must be a number.";
            return;
        }
        if (!SslModes.Contains(DbSslMode, StringComparer.OrdinalIgnoreCase))
        {
            FormError = "Invalid SSL mode.";
            return;
        }

        Busy = true;
        NotifyStateChanged();
        try
        {
            var values = new Dictionary<string, string>
            {
                [ProjectDatabaseConnectionResolver.HostVariable] = DbHost.Trim(),
                [ProjectDatabaseConnectionResolver.UsernameVariable] = DbUser.Trim(),
                [ProjectDatabaseConnectionResolver.PasswordVariable] = DbPassword,
                [ProjectDatabaseConnectionResolver.SslModeVariable] = DbSslMode.Trim(),
            };
            if (port is not null)
                values[ProjectDatabaseConnectionResolver.PortVariable] = port;
            if (!string.IsNullOrWhiteSpace(DbName))
                values[ProjectDatabaseConnectionResolver.NameVariable] = DbName.Trim();
            await WriteSecretsAsync(id, values);
            Message = $"External database for '{SelectedProjectName}' saved. This project now runs against it; reset to return to the cluster database.";
            MessageIsError = false;
            ClearForms();
            await LoadSecretsAsync();
        }
        catch (Exception ex)
        {
            Message = ex.Message;
            MessageIsError = true;
        }
        finally
        {
            Busy = false;
            NotifyStateChanged();
        }
    }

    public async Task ResetExternalDatabaseAsync()
    {
        if (SelectedProjectId is not { } id) return;
        Busy = true;
        NotifyStateChanged();
        try
        {
            foreach (var key in ExternalDatabaseKeys)
                await service.DeleteProjectSecretAsync(id, key);
            Message = $"Project '{SelectedProjectName}' reverted to the cluster database.";
            MessageIsError = false;
            ClearForms();
            await LoadSecretsAsync();
        }
        catch (Exception ex)
        {
            Message = ex.Message;
            MessageIsError = true;
        }
        finally
        {
            Busy = false;
            NotifyStateChanged();
        }
    }

    public async Task SaveExternalIndexAsync()
    {
        FormError = null;
        if (SelectedProjectId is not { } id)
        {
            FormError = "Select a project first.";
            return;
        }
        if (string.IsNullOrWhiteSpace(OsEndpoint))
        {
            FormError = "Endpoint is required.";
            return;
        }
        if (!Uri.TryCreate(OsEndpoint.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https"))
        {
            FormError = "Endpoint must be an absolute HTTP or HTTPS URL.";
            return;
        }

        Busy = true;
        NotifyStateChanged();
        try
        {
            var values = new Dictionary<string, string>
            {
                [OpenSearchConnectionResolver.EndpointVariable] = OsEndpoint.Trim().TrimEnd('/'),
            };
            if (!string.IsNullOrWhiteSpace(OsUsername))
                values[OpenSearchConnectionResolver.UsernameVariable] = OsUsername.Trim();
            if (!string.IsNullOrWhiteSpace(OsPassword))
                values[OpenSearchConnectionResolver.PasswordVariable] = OsPassword;
            if (!string.IsNullOrWhiteSpace(OsIndex))
                values[OpenSearchConnectionResolver.IndexVariable] = OsIndex.Trim();
            await WriteSecretsAsync(id, values);
            Message = $"External index for '{SelectedProjectName}' saved. Data Search now targets it; reset to fall back to the workspace default.";
            MessageIsError = false;
            ClearForms();
            await LoadSecretsAsync();
        }
        catch (Exception ex)
        {
            Message = ex.Message;
            MessageIsError = true;
        }
        finally
        {
            Busy = false;
            NotifyStateChanged();
        }
    }

    public async Task ResetExternalIndexAsync()
    {
        if (SelectedProjectId is not { } id) return;
        Busy = true;
        NotifyStateChanged();
        try
        {
            foreach (var key in ExternalIndexKeys)
                await service.DeleteProjectSecretAsync(id, key);
            Message = $"Project '{SelectedProjectName}' reverted to the workspace OpenSearch default.";
            MessageIsError = false;
            ClearForms();
            await LoadSecretsAsync();
        }
        catch (Exception ex)
        {
            Message = ex.Message;
            MessageIsError = true;
        }
        finally
        {
            Busy = false;
            NotifyStateChanged();
        }
    }

    // Vault secrets are immutable — replace is delete-then-recreate.
    private async Task WriteSecretsAsync(Guid projectId, IReadOnlyDictionary<string, string> values)
    {
        foreach (var (key, value) in values)
        {
            await service.DeleteProjectSecretAsync(projectId, key);
            await service.AddProjectSecretAsync(projectId, key, value);
        }
    }

    private void ClearForms()
    {
        DbHost = "";
        DbPort = "";
        DbName = "";
        DbUser = "";
        DbPassword = "";
        DbSslMode = "Prefer";
        OsEndpoint = "";
        OsUsername = "";
        OsPassword = "";
        OsIndex = "";
    }
}
