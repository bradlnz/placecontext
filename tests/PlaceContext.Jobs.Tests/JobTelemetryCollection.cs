using Xunit;

namespace PlaceContext.Jobs.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class JobTelemetryCollection
{
    public const string Name = "Job telemetry";
}
