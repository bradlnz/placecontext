namespace PlaceContext.Host.Controllers.Api;

public sealed class TriggerChainRequest
{
    /// <summary>Optional input payload for the first stage (JSON string). Omit to use the first
    /// job's stored shard payloads.</summary>
    public string? InputPayload
    {
        get; set;
    }
}
