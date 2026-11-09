namespace PlaceContext.Host.Components.ViewModels;

public static class ParameterInputCatalog
{
    public static ParameterInputType Parse(string? value) =>
        value?.ToLowerInvariant() switch
        {
            "select" => ParameterInputType.Select,
            "number" => ParameterInputType.Number,
            "date" => ParameterInputType.Date,
            "datetime" or "datetime-local" => ParameterInputType.DateTime,
            "time" => ParameterInputType.Time,
            "checkbox" => ParameterInputType.Checkbox,
            "file" => ParameterInputType.File,
            _ => ParameterInputType.Text,
        };
}
