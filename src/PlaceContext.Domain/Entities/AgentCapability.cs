namespace PlaceContext.Domain.Entities;

public enum AgentCapability
{
    GraphRead = 0,
    DataRead = 1,
    ArtifactsRead = 2,
    JobsRead = 3,
    JobsRun = 4,
    ChainsRead = 5,
    ChainsRun = 6,
    SchedulesRead = 7,
    SchedulesManage = 8,
    McpCall = 9,
}
