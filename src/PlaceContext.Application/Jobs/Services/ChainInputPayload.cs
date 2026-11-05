using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.Json;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Features;

public static class ChainInputPayload
{
    public static JsonObject Object(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return new();
        }
        try
        {
            var node = JsonNode.Parse(payload);
            return node as JsonObject ?? new JsonObject { ["previous"] = node };
        }
        catch (JsonException)
        {
            return new JsonObject { ["previous"] = payload };
        }
    }

    public static JsonNode? Get(JsonObject payload, string name)
    {
        if (payload.TryGetPropertyValue(name, out var flat))
        {
            return flat;
        }
        JsonNode? value = payload;
        foreach (var part in name.Split('.'))
        {
            value = value is JsonObject obj ? obj[part] : null;
        }
        return value;
    }

    private static void Merge(JsonObject target, JsonObject source)
    {
        foreach (var (key, value) in source)
        {
            if (value is JsonObject obj && target[key] is JsonObject existing)
            {
                Merge(existing, obj);
                continue;
            }
            target[key] = value?.DeepClone();
        }
    }

    public static bool Missing(JsonNode? value) => value is null or JsonObject { Count: 0 } or JsonArray { Count: 0 }
        || value is JsonValue scalar && scalar.TryGetValue<string>(out var text) && string.IsNullOrWhiteSpace(text);

    public static void Set(JsonObject payload, string name, JsonNode? value)
    {
        var parts = name.Split('.');
        if (parts.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Invalid input field path.");
        }
        var target = payload;
        foreach (var part in parts[..^1])
        {
            if (target[part] is not JsonObject child)
            {
                child = new JsonObject();
                target[part] = child;
            }
            target = child;
        }
        target[parts[^1]] = value?.DeepClone();
        if (parts.Length > 1)
        {
            payload.Remove(name);
        }
    }

    // Overrides are keyed by flat step index, exactly like the existing run parameter form.
    public static Dictionary<int, string> Apply(ChainInputRequest request, string valuesJson,
        string? overridesJson, string? currentPayload)
    {
        var values = JsonNode.Parse(valuesJson) as JsonObject
            ?? throw new ArgumentException("Input values must be an object keyed by step index.");
        var overrides = overridesJson is null ? new Dictionary<int, string>()
            : JsonSerializer.Deserialize<Dictionary<int, string>>(overridesJson)!;
        if (values.Any(pair => !request.Steps.Any(step => step.Index.ToString() == pair.Key)))
        {
            throw new ArgumentException("Unknown input step.");
        }
        foreach (var step in request.Steps)
        {
            var supplied = values[step.Index.ToString()] as JsonObject
                ?? throw new ArgumentException($"Inputs for step {step.Index} are required.");
            if (supplied.Any(pair => !step.Parameters.Any(p => p.Name == pair.Key)))
            {
                throw new ArgumentException("Unknown input field.");
            }
            if (request.Kind is Domain.ValueObjects.LoopChainAction.HumanType or Domain.ValueObjects.LoopChainAction.AgentType or Domain.ValueObjects.LoopChainAction.TaskType)
            {
                foreach (var field in step.Parameters)
                {
                    if (request.ResponseType == Domain.ValueObjects.LoopChainAction.YesNoResponse)
                    {
                        if (supplied[field.Name] is not JsonValue decision || !decision.TryGetValue<bool>(out _))
                        {
                            throw new ArgumentException("Choose Yes or No to complete this step.");
                        }
                        continue;
                    }
                    if (supplied[field.Name] is not JsonValue response
                                            || !response.TryGetValue<string>(out var text) || string.IsNullOrWhiteSpace(text))
                    {
                        throw new ArgumentException("A new text response is required to complete this step.");
                    }
                }
            }
            var payload = Object(overrides.GetValueOrDefault(step.Index));
            foreach (var field in step.Parameters)
            {
                if (!supplied.TryGetPropertyValue(field.Name, out var value))
                {
                    continue;
                }
                if (field.Required && Missing(value))
                {
                    throw new ArgumentException($"{field.Label ?? field.Name} is required.");
                }
                if (!Missing(value) && field.Type == "number"
                                    && (!double.TryParse(value!.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || !double.IsFinite(number)))
                {
                    throw new ArgumentException($"{field.Label ?? field.Name} must be a number.");
                }
                Set(payload, field.Name, value);
            }
            var effective = Object(currentPayload);
            Merge(effective, payload);
            foreach (var field in step.Parameters.Where(field => field.Required))
            {
                if (Missing(Get(effective, field.Name)))
                {
                    throw new ArgumentException($"{field.Label ?? field.Name} is required.");
                }
            }
            overrides[step.Index] = payload.ToJsonString();
        }
        return overrides;
    }
}
