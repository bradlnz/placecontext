using System.Text.Json.Nodes;
using System.Text.Json;
using System.Globalization;
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
    public static JobParameterDto ChainAttachmentParameter
    {
        get;
    } = new(
        ChainAttachmentField,
        "File attachment",
        Required: false,
        Type: "file"
    );

    public Dictionary<string, string> Args { get; private set; } = new(StringComparer.Ordinal);
    public string? Error
    {
        get; private set;
    }

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

    /// <summary>UI-only form key for a workflow step parameter (never sent on the wire).</summary>
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
        {
            return true;
        }
        Error = $"Required: {string.Join(", ", missing)}";
        return false;
    }

    public bool ValidateJobParameters(IEnumerable<JobParameterDto> parameters) =>
        ValidateFields(parameters.Select(field => (field.Name, field, field.Label ?? field.Name)));

    public bool ValidateChainStepParameters(IEnumerable<(int Index, JobView Job)> steps) =>
        ValidateFields(steps.SelectMany(step => step.Job.Parameters.Select(field =>
            (ChainArgKey(step.Index, field.Name), field, $"step {step.Index + 1}: {field.Label ?? field.Name}"))));

    private bool ValidateFields(IEnumerable<(string Key, JobParameterDto Field, string Label)> fields)
    {
        var entries = fields.ToArray();
        if (!ValidateRequired(entries.Select(item => (item.Key, item.Label, item.Field.Required)))) return false;
        foreach (var (key, field, label) in entries)
        {
            var value = Get(key);
            if (string.IsNullOrWhiteSpace(value)) continue;
            var parsed = TypedValue(field.Type, value);
            var valid = field.Type switch
            {
                "uuid" or "guid" => Guid.TryParse(value, out _),
                "integer" or "int" => parsed is long,
                "number" => parsed is decimal or double,
                "checkbox" or "boolean" or "bool" => parsed is bool,
                "json" => parsed is JsonElement,
                _ => true
            };
            if (valid) continue;
            Error = $"{label} must be a valid {field.Type}.";
            return false;
        }
        return true;
    }

    private static object? TypedValue(string type, string value)
    {
        switch (type)
        {
            case "integer" or "int":
                if (string.IsNullOrWhiteSpace(value)) return null;
                if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer)) return integer;
                break;
            case "number":
                if (string.IsNullOrWhiteSpace(value)) return null;
                if (decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)) return number;
                if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var large) && double.IsFinite(large)) return large;
                break;
            case "checkbox" or "boolean" or "bool":
                if (string.IsNullOrWhiteSpace(value)) return false;
                if (bool.TryParse(value, out var boolean)) return boolean;
                break;
            case "json":
                if (string.IsNullOrWhiteSpace(value)) return null;
                try { using var document = JsonDocument.Parse(value); return document.RootElement.Clone(); }
                catch (JsonException) { }
                break;
        }
        // Keep incomplete editor drafts; run forms validate before submission.
        return value;
    }

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
                    : TypedValue(parameter.Type, value);
        }
        return JsonSerializer.Serialize(payload);
    }

    private static bool TryParseFileReference(string value, out JsonElement reference)
    {
        reference = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }
        try
        {
            using var document = JsonDocument.Parse(value);
            if (
                document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("$file", out var file)
                || file.ValueKind != JsonValueKind.Object
            )
            {
                return false;
            }
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
        {
            return payload;
        }
        JsonObject result;
        JsonObject CreatePayload()
        {
            JsonObject resolvedResult;
            if (string.IsNullOrWhiteSpace(payload))
            {
                resolvedResult = new JsonObject();
                return resolvedResult;
            }
            try
            {
                var existing = JsonNode.Parse(payload);
                void ApplyPayload()
                {
                    if (existing is JsonObject existingObject)
                    {
                        resolvedResult = (JsonObject)existingObject.DeepClone();
                        return;
                    }
                    resolvedResult = new JsonObject { ["previous"] = existing?.DeepClone() };
                }
                ApplyPayload();
            }
            catch (JsonException)
            {
                resolvedResult = new JsonObject { ["previous"] = payload };
            }
            return resolvedResult;
        }
        result = CreatePayload();
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
