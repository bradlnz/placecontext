using System.Text.Json.Nodes;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Host.Components.ViewModels.Helpers;

namespace PlaceContext.Host.Components.ViewModels;

public sealed partial class JobChainsViewModel
{
    public sealed class RunInputField
    {
        public string Name { get; set; } = "";
        public string Type { get; set; } = "text";
        public string Value { get; set; } = "";
        public JobParameterDto? Definition { get; init; }
        public JobParameterDto Parameter => Definition ?? new(Name, string.IsNullOrWhiteSpace(Name) ? "Input value" : Name, Required: false, Type: Type);
    }

    public List<RunInputField> RunInputFields { get; } = [];
    public void AddRunInputField() { RunInputFields.Add(new()); NotifyStateChanged(); }
    public void RemoveRunInputField(RunInputField field) { if (field.Definition is null) RunInputFields.Remove(field); NotifyStateChanged(); }
    public void GenerateRunInputDefault(RunInputField field)
    {
        if (string.IsNullOrWhiteSpace(field.Value)) field.Value = RunInputDefaults.Generate(field.Parameter);
    }

    private string? AddRunInputFields(string? payload)
    {
        var fields = RunInputFields.Where(field => !string.IsNullOrWhiteSpace(field.Name) || !string.IsNullOrWhiteSpace(field.Value)).ToArray();
        var names = new HashSet<string>(StringComparer.Ordinal);
        var prompt = new ParameterPromptState();
        foreach (var field in fields)
        {
            var name = field.Name.Trim();
            if (name.Split('.').Any(string.IsNullOrWhiteSpace))
                throw new ArgumentException("Give each input field a name, such as question or customer.id.");
            if (!names.Add(name)) throw new ArgumentException($"Input field '{name}' is repeated.");
            if (RunPromptSteps.Any(step => step.Index == 0 && step.Job.Parameters.Any(parameter => parameter.Name == name)))
                throw new ArgumentException($"Edit '{name}' in the step inputs instead of adding it twice.");
            prompt.Set(name, field.Value);
        }
        var parameters = fields.Select(field => field.Parameter with { Name = field.Name.Trim() }).ToArray();
        if (!prompt.ValidateJobParameters(parameters)) throw new ArgumentException(prompt.Error);
        if (fields.Length == 0) return payload;
        var result = ChainInputPayload.Object(payload);
        var additional = JsonNode.Parse(prompt.ToJobPayload(parameters))!.AsObject();
        foreach (var (name, value) in additional) ChainInputPayload.Set(result, name, value);
        return result.ToJsonString();
    }
}
