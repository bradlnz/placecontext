using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using PlaceContext.Application;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Host;

namespace PlaceContext.Host.Components.ViewModels;

public sealed class DataEntitiesViewModel : PageViewModel
{
    private readonly IPlaceContextService _service;
    private readonly PortalUiState _ui;
    private readonly NavigationManager _navigation;

    public DataEntitiesViewModel(
        IPlaceContextService service,
        PortalUiState ui,
        NavigationManager navigation
    ) => (_service, _ui, _navigation) = (service, ui, navigation);

    public Guid ProjectId { get; private set; }
    public IReadOnlyList<DataEntityView> Entities { get; private set; } =
        Array.Empty<DataEntityView>();
    public IReadOnlyList<ProjectTableInfo> Tables { get; private set; } =
        Array.Empty<ProjectTableInfo>();
    public bool ShowEditor { get; set; }
    public bool Busy { get; private set; }
    public Guid? EditId { get; private set; }
    public string EditName { get; set; } = "";
    public string EditTable { get; set; } = "";
    public string EditLabel { get; set; } = "";
    public string? Error { get; private set; }
    public List<RelationEdit> EditRelations { get; } = new();
    public List<string> EditTags { get; } = new();
    public string TagInput { get; set; } = "";
    public bool Rescanning { get; private set; }
    public string? RescanMessage { get; private set; }

    public sealed class RelationEdit
    {
        public string Column { get; set; } = "";
        public string TargetEntity { get; set; } = "";
        public string TargetColumn { get; set; } = "";
    }

    public async Task LoadAsync(Guid projectId)
    {
        ProjectId = projectId;
        _ui.Set("Entities", "the project's data graph — tagged tables, related records");
        try
        {
            Entities = await _service.ListDataEntitiesAsync(projectId);
            Tables = await _service.ListProjectDataTablesAsync(projectId);
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        NotifyStateChanged();
    }

    public void NavigateToEntity(string name) =>
        _navigation.NavigateTo($"/project/{ProjectId}/entity/{Uri.EscapeDataString(name)}");

    public async Task RescanAsync()
    {
        Rescanning = true;
        RescanMessage = null;
        try
        {
            var result = await _service.RescanRecordLinksAsync(ProjectId);
            RescanMessage =
                $"scanned {result.TablesScanned} table(s) · {result.LinksFound} link(s)";
        }
        catch (Exception ex)
        {
            RescanMessage = ex.Message;
        }
        finally
        {
            Rescanning = false;
            NotifyStateChanged();
        }
    }

    public void OpenEditor(DataEntityView? entity)
    {
        EditId = entity?.Id;
        EditName = entity?.Name ?? "";
        EditTable = entity?.TableName ?? "";
        EditLabel = entity?.LabelColumn ?? "";
        EditRelations.Clear();
        if (entity is not null)
            EditRelations.AddRange(
                entity.Relations.Select(r => new RelationEdit
                {
                    Column = r.Column,
                    TargetEntity = r.TargetEntity,
                    TargetColumn = r.TargetColumn,
                })
            );
        EditTags.Clear();
        if (entity is not null)
            EditTags.AddRange(entity.Tags);
        TagInput = "";
        Error = null;
        ShowEditor = true;
        NotifyStateChanged();
    }

    public void CloseEditor() => ShowEditor = false;

    public void TagKey(KeyboardEventArgs args)
    {
        if (args.Key is not ("Enter" or ","))
            return;
        var tag = TagInput.Trim().TrimEnd(',').Trim();
        if (tag.Length > 0 && !EditTags.Contains(tag, StringComparer.OrdinalIgnoreCase))
            EditTags.Add(tag);
        TagInput = "";
    }

    public async Task SaveAsync()
    {
        Error = null;
        if (string.IsNullOrWhiteSpace(EditName))
        {
            Error = "Name is required.";
            return;
        }
        if (string.IsNullOrWhiteSpace(EditTable))
        {
            Error = "Pick a source table or view.";
            return;
        }
        Busy = true;
        try
        {
            var tags = EditTags.ToList();
            var pending = TagInput.Trim().TrimEnd(',').Trim();
            if (pending.Length > 0 && !tags.Contains(pending, StringComparer.OrdinalIgnoreCase))
                tags.Add(pending);
            await _service.SaveDataEntityAsync(
                new SaveDataEntityCommand(
                    ProjectId,
                    EditName,
                    EditTable,
                    EditLabel,
                    EditRelations
                        .Select(r => new EntityRelationDto(
                            r.Column,
                            r.TargetEntity,
                            r.TargetColumn
                        ))
                        .ToList(),
                    tags,
                    EditId
                )
            );
            await LoadAsync(ProjectId);
            ShowEditor = false;
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        finally
        {
            Busy = false;
            NotifyStateChanged();
        }
    }

    public async Task DeleteAsync()
    {
        if (EditId is not { } id)
            return;
        try
        {
            await _service.DeleteDataEntityAsync(id);
            await LoadAsync(ProjectId);
            ShowEditor = false;
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
    }
}
