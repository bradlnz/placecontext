namespace PlaceContext.Infrastructure.Build;

internal static class ModelProtocols
{
    private static readonly IReadOnlyDictionary<string, IModelProtocol> Providers = new Dictionary<string, IModelProtocol>(StringComparer.Ordinal)
    {
        ["openai"] = new OpenAiModelProtocol(),
        ["anthropic"] = new AnthropicModelProtocol()
    };

    internal static bool Supports(string protocol) => Providers.ContainsKey(protocol);
    internal static IModelProtocol Get(string protocol) => Providers.TryGetValue(protocol, out var provider)
        ? provider : throw new ArgumentException("Unsupported model protocol.", nameof(protocol));
}
