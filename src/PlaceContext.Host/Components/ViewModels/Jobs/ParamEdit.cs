using PlaceContext.Application.Dtos;

namespace PlaceContext.Host.Components.ViewModels;

public sealed class ParamEdit
{
    public string Name = "";
    public string Label = "";
    public string Type = "text";
    public string OptionsRaw = "";
    public bool Required = true;

    public JobParameterDto ToDto() => new(Name.Trim(),
        string.IsNullOrWhiteSpace(Label) ? null : Label.Trim(), Required, Type,
        Type is "select" or "file"
            ? OptionsRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : null);

    public static ParamEdit From(JobParameterDto p) => new()
    {
        Name = p.Name,
        Label = p.Label ?? "",
        Type = p.Type,
        OptionsRaw = string.Join(", ", p.Options ?? Array.Empty<string>()),
        Required = p.Required,
    };
}
