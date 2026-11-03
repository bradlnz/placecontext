using PlaceContext.Application.Shared;
namespace PlaceContext.Application.Jobs;

public static class JobAuthoringGuide
{
    public static string Text => PromptText.Read("Jobs.Prompts.JobAuthoringGuide");
}
