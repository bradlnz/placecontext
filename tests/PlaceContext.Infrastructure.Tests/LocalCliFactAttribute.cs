namespace PlaceContext.Infrastructure.Tests;

internal sealed class LocalCliFactAttribute : FactAttribute
{
    public LocalCliFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("PLACECONTEXT_TEST_LOCAL_CLI") != "1")
        {
            Skip = "Opt in with PLACECONTEXT_TEST_LOCAL_CLI=1; uses installed CLI accounts and remote inference.";
        }
    }
}
