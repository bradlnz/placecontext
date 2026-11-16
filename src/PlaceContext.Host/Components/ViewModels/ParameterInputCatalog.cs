namespace PlaceContext.Host.Components.ViewModels;

public static class ParameterInputCatalog
{
    public static ParameterInputType Parse(string? value) =>
        value?.ToLowerInvariant() switch
        {
            "select" => ParameterInputType.Select,
            "number" or "integer" or "int" => ParameterInputType.Number,
            "date" => ParameterInputType.Date,
            "datetime" or "datetime-local" => ParameterInputType.DateTime,
            "time" => ParameterInputType.Time,
            "checkbox" or "boolean" or "bool" => ParameterInputType.Checkbox,
            "file" => ParameterInputType.File,
            "json" => ParameterInputType.Json,
            _ => ParameterInputType.Text,
        };
}
