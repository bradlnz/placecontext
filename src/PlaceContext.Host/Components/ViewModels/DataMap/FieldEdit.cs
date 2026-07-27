using PlaceContext.Application.Shared;

namespace PlaceContext.Host.Components.ViewModels;

public sealed class FieldEdit
{
    public string SourcePath { get; set; } = "";
    public string Column { get; set; } = "";
    public string Type { get; set; } = DataColumnTypes.Text;
}
