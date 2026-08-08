namespace PlaceContext.Host.Components.ViewModels;

public sealed class DataEntityRelationEdit
{
    public string Column { get; set; } = string.Empty;
    public string TargetEntity { get; set; } = string.Empty;
    public string TargetColumn { get; set; } = string.Empty;
}
