using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Workload;

public sealed class ExecutionSafetyException(string message, Exception? inner = null) : UnauthorizedAccessException(message, inner)
{
    public const string ActivityKind = "execution_blocked";
    public static bool IsBlocked(ChainRunView run) => run.Steps.Any(step =>
        step.AgentActivity?.Any(activity => activity.Kind == ActivityKind) == true);
}
