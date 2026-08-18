using System.Text.Json;
using System.Text.Json.Nodes;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Host.Components.ViewModels.Helpers;

/// <summary>
/// Shared run-parameter prompt state for Jobs and JobChains.
/// Form keys may be plain param names (single job) or disambiguated keys like
/// <c>step0:address</c> (chain) — those keys never leave the UI.
/// </summary>
public sealed class ParameterPromptState
{
    public const string ChainAttachmentField = "attachment";
    public static JobParameterDto ChainAttachmentParameter { get; } = new(
        ChainAttachmentField,
        "File attachment",
        Required: false,
        Type: "file"
    );

    public Dictionary<string, string> Args { get; private set; } = new(StringComparer.Ordinal);
    public string? Error { get; private set; }

    public string Get(string key) => Args.TryGetValue(key, out var v) ? v : "";

    public void Set(string key, string value) => Args[key] = value;

    public void Clear()
    {
        Args = new Dictionary<string, string>(StringComparer.Ordinal);
        Error = null;
    }

    public void Reset(IEnumerable<KeyValuePair<string, string>> initial)
    {
        Args = new Dictionary<string, string>(initial, StringComparer.Ordinal);
        Error = null;
    }

    public void SetError(string? error) => Error = error;

    /// <summary>UI-only form key for a chain step parameter (never sent on the wire).</summary>
    public static string ChainArgKey(int stepIndex, string param) => $"step{stepIndex}:{param}";

    /// <summary>
    /// Validate required fields. <paramref name="fields"/> is (formKey, displayLabel, required).
    /// </summary>
    public bool ValidateRequired(IEnumerable<(string Key, string Label, bool Required)> fields)
    {
        Error = null;
        var missing = fields
            .Where(f => f.Required && string.IsNullOrWhiteSpace(Get(f.Key)))
            .Select(f => f.Label)
            .ToList();
        if (missing.Count == 0)
            return true;
        Error = $"Required: {string.Join(", ", missing)}";
        return false;
    }

    public bool ValidateJobParameters(IEnumerable<JobParameterDto> parameters) =>
        ValidateRequired(parameters.Select(p => (p.Name, p.Label ?? p.Name, p.Required)));

    public bool ValidateChainStepParameters(IEnumerable<(int Index, JobView Job)> steps) =>
        ValidateRequired(
            steps.SelectMany(s =>
                s.Job.Parameters.Select(p =>
                    (
                        ChainArgKey(s.Index, p.Name),
                        $"step {s.Index + 1}: {p.Label ?? p.Name}",
                        p.Required
                    )
                )
            )
        );

    /// <summary>Serialize bare parameter names → JSON object for job stdin / step override.</summary>
    public static string ToJsonPayload(
        IEnumerable<JobParameterDto> parameters,
        Func<string, string> valueForName
    )
    {
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var parameter in parameters)
        {
            var value = valueForName(parameter.Name);
            payload[parameter.Name] =
                parameter.Type == "file" && TryParseFileReference(value, out var reference)
                    ? reference
                    : value;
        }
        return JsonSerializer.Serialize(payload);
    }

    private static bool TryParseFileReference(string value, out JsonElement reference)
    {
        reference = default;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        try
        {
            using var document = JsonDocument.Parse(value);
            if (
                document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("$file", out var file)
                || file.ValueKind != JsonValueKind.Object
            )
                return false;

            reference = document.RootElement.Clone();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public string ToJobPayload(IEnumerable<JobParameterDto> parameters) =>
        ToJsonPayload(parameters, Get);

    /// <summary>
    /// Adds the optional chain attachment to an initial JSON payload. Object payloads are merged;
    /// non-object JSON is retained under <c>previous</c>. Invalid file markers are ignored.
    /// </summary>
    public static string? WithChainAttachment(string? payload, string? marker)
    {
        if (!TryParseFileReference(marker ?? "", out var reference))
            return payload;

        JsonObject result;
        if (string.IsNullOrWhiteSpace(payload))
        {
            result = new JsonObject();
        }
        else
        {
            try
            {
                var existing = JsonNode.Parse(payload);
                if (existing is JsonObject existingObject)
                    result = (JsonObject)existingObject.DeepClone();
                else
                    result = new JsonObject { ["previous"] = existing?.DeepClone() };
            }
            catch (JsonException)
            {
                result = new JsonObject { ["previous"] = payload };
            }
        }

        result[ChainAttachmentField] = JsonNode.Parse(reference.GetRawText());
        return result.ToJsonString();
    }

    /// <summary>
    /// Group chain form values into per-step JSON overrides keyed by flat step index.
    /// Wire shape uses bare param names; form keys stay <c>stepN:param</c>.
    /// </summary>
    public IReadOnlyDictionary<int, string> ToStepPayloadOverrides(
        IEnumerable<(int Index, JobView Job)> steps
    ) =>
        steps
            .Where(s => s.Job.Parameters.Count > 0)
            .ToDictionary(
                s => s.Index,
                s => ToJsonPayload(s.Job.Parameters, name => Get(ChainArgKey(s.Index, name)))
            );
}
