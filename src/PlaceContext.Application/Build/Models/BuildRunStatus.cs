using System.Text.Json.Nodes;

namespace PlaceContext.Application.Build;

public enum BuildRunStatus
{
    Idle, Queued, Running, Completed, Stopped, Failed, Interrupted
}
