namespace PlaceContext.Application.Jobs;

/// <summary>The project authorized by the authenticated job credential, never by request input.</summary>
public interface IJobDataAccess
{
    Guid RequireProjectId();
    Guid RequireSourceId();
}
