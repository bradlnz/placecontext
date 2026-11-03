namespace PlaceContext.Infrastructure.Workload;

public sealed class VirtualMachineAgentOptions
{
    public string Provider { get; set; } = "chat-completions";
    public string Endpoint { get; set; } = "";
    public string Model { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public int MaxSteps { get; set; } = 20;
}
