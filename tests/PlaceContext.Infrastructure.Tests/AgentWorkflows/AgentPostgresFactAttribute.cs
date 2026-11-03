namespace PlaceContext.Infrastructure.Tests.AgentWorkflows;

public sealed class AgentPostgresFactAttribute : FactAttribute
{
    public AgentPostgresFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("PLACECONTEXT_TEST_POSTGRES") is null)
        {
            Skip = "Set PLACECONTEXT_TEST_POSTGRES to an isolated test database.";
        }
    }
}
