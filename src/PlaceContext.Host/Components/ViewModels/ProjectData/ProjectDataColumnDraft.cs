using PlaceContext.Application.Shared;

namespace PlaceContext.Host.Components.ViewModels;

public sealed class ProjectDataColumnDraft
{
    public string Name = string.Empty;
    public string Type = DataColumnTypes.Text;
    public bool NotNull;
    public bool PrimaryKey;
}
