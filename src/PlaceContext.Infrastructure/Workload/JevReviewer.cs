using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using PlaceContext.Application.Ports;

namespace PlaceContext.Infrastructure.Workload;

public sealed class JevReviewer(IHttpClientFactory clients, IOptions<TypeSafeOptions> options, TypeSafeConfiguration? configuration = null) : IJevReviewer
{
    public async Task<JsonObject> EvaluateAsync(string question, string? state, CancellationToken ct, Guid? projectId = null)
    {
        var config = configuration is null ? options.Value : await configuration.ResolveAsync(projectId, ct);
        if (string.IsNullOrWhiteSpace(config.ApiKey) || config.ApiKey.Any(c => char.IsWhiteSpace(c) || char.IsControl(c))
            || string.IsNullOrWhiteSpace(config.Model))
            throw new InvalidOperationException("Configure the TypeSafe API key and model (PlaceContext:TypeSafe) before running a Jev step.");
        if (string.IsNullOrWhiteSpace(question) || question.Length > 4000)
            throw new ArgumentException("Jev requires a verification question of up to 4000 characters.");
        if (state?.Length > 65_536)
            throw new InvalidOperationException("The workflow output is too large for Jev review. Reduce it to 65,536 characters; verification input is not truncated.");

        JsonNode? context;
        try { context = JsonNode.Parse(state ?? "{}"); }
        catch (JsonException) { context = JsonValue.Create(state); }
        if (context is not (JsonObject or JsonArray) && !(context is JsonValue value && value.TryGetValue<string>(out _)))
            context = JsonValue.Create(state ?? "{}");

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(90));
        using var client = clients.CreateClient("typesafe");
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.typesafe.ai/v1/systemone")
        {
            Content = JsonContent.Create(new { model = config.Model, state = context,
                questions = new { verification = new { type = "noul", instructions = question } } }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey);
        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"Jev returned HTTP {(int)response.StatusCode}. Check the TypeSafe API key, quota, and request limits before retrying.");
            await response.Content.LoadIntoBufferAsync(65_536, deadline.Token);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(deadline.Token));
            var root = body.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("model", out var model) || model.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(model.GetString())
                || !root.TryGetProperty("answers", out var answers) || answers.ValueKind != JsonValueKind.Object
                || !answers.TryGetProperty("verification", out var answer) || answer.ValueKind != JsonValueKind.Object
                || !answer.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String || type.GetString() != "noul"
                || !answer.TryGetProperty("noul", out var noul) || noul.ValueKind != JsonValueKind.Number
                || !noul.TryGetDouble(out var probability) || !double.IsFinite(probability) || probability is < 0 or > 1)
                throw new InvalidOperationException("Jev returned an invalid verification response.");
            return new JsonObject { ["type"] = "noul", ["noul"] = probability, ["model"] = model.GetString(), ["question"] = question };
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { throw new InvalidOperationException("Jev verification timed out. The workflow has not been approved."); }
        catch (HttpRequestException)
        { throw new InvalidOperationException("Could not reach TypeSafe or read its response. Retry the Jev step when the service is available."); }
        catch (JsonException)
        { throw new InvalidOperationException("Jev returned an invalid verification response."); }
    }
}
