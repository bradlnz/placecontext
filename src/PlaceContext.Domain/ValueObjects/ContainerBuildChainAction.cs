namespace PlaceContext.Domain.ValueObjects;

public sealed class ContainerBuildChainAction : ChainAction
{
    public const string ActionType = "container_build";
    public ContainerBuildChainAction(string dockerfile, string contextField = "files", string outputField = "container_build", int timeoutSeconds = 600, bool allowNetwork = false)
    {
        if (string.IsNullOrWhiteSpace(dockerfile) || dockerfile.Length > 65536 || dockerfile.Contains('\0'))
        {
            throw new ArgumentException("Enter a Dockerfile up to 65,536 characters.");
        }

        ContainerRunChainAction.ValidateField(contextField);
        ContainerRunChainAction.ValidateField(outputField, nested: false);
        if (timeoutSeconds is < 10 or > 3600)
        {
            throw new ArgumentException("Timeout must be 10–3600 seconds.");
        }

        Dockerfile = dockerfile;
        ContextField = contextField;
        OutputField = outputField;
        TimeoutSeconds = timeoutSeconds;
        AllowNetwork = allowNetwork;
    }
    public override string Type => ActionType;
    public override string DisplayName => "Build container";
    public string Dockerfile
    {
        get;
    }
    public string ContextField
    {
        get;
    }
    public string OutputField
    {
        get;
    }
    public int TimeoutSeconds
    {
        get;
    }
    public bool AllowNetwork
    {
        get;
    }
}
