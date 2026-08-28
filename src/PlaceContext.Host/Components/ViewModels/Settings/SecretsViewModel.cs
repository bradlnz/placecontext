using PlaceContext.Application;
using PlaceContext.Application.Dtos;
using PlaceContext.Host;

namespace PlaceContext.Host.Components.ViewModels;

public sealed class SecretsViewModel(PlaceContextService service, PortalUiState ui) : PageViewModel
{
    public const string PageTitle = "Vault";
    public Guid ProjectId { get; set; }
    public IReadOnlyList<ProjectSecretView>? Secrets { get; private set; }
    public bool Loading { get; private set; } = true;
    public bool Busy { get; private set; }
    public string? Message { get; private set; }
    public string? AddError { get; private set; }
    public string NewName { get; set; } = "";
    public string NewValue { get; set; } = "";

    public string CreatedAt(ProjectSecretView secret) =>
        Presentation.Format(secret.CreatedAt, "yyyy-MM-dd HH:mm");

    public async Task LoadAsync(Guid projectId)
    {
        ProjectId = projectId;
        Loading = true;
        Message = null;
        NotifyStateChanged();
        try
        {
            Secrets = await service.ListProjectSecretsAsync(projectId);
            ui.Set(PageTitle, "encrypted project secrets");
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

    public async Task AddAsync()
    {
        AddError = null;
        if (string.IsNullOrWhiteSpace(NewName))
        {
            AddError = "Name is required.";
            return;
        }
        if (string.IsNullOrEmpty(NewValue))
        {
            AddError = "Value is required.";
            return;
        }
        Busy = true;
        NotifyStateChanged();
        var name = NewName.Trim();
        try
        {
            await service.AddProjectSecretAsync(ProjectId, name, NewValue);
            await LoadAsync(ProjectId);
            Message = $"Secret '{name}' saved.";
            NewName = "";
            NewValue = "";
        }
        catch (Exception ex)
        {
            AddError = ex.Message;
        }
        finally
        {
            Busy = false;
            NotifyStateChanged();
        }
    }

    public async Task DeleteAsync(string name)
    {
        Busy = true;
        Message = null;
        NotifyStateChanged();
        try
        {
            await service.DeleteProjectSecretAsync(ProjectId, name);
            await LoadAsync(ProjectId);
            Message = $"Secret '{name}' deleted.";
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
