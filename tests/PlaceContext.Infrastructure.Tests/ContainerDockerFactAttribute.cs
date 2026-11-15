namespace PlaceContext.Infrastructure.Tests;

public sealed class ContainerDockerFactAttribute : FactAttribute
{
    public ContainerDockerFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("PLACECONTEXT_CONTAINER_SMOKE") != "1")
        {
            Skip = "Set PLACECONTEXT_CONTAINER_SMOKE=1 to test Docker/buildx execution.";
        }
    }
}
