namespace PlaceContext.Application.Build;

public sealed class ModelCredentials(string apiKey, string model)
{
    public string ApiKey { get; } = apiKey;
    public string Model { get; } = model;
}
