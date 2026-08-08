namespace PlaceContext.Application.Ports;

/// <summary>Sets and clears the caller context for the current asynchronous request flow.</summary>
public interface ICurrentUserAccessor
{
    void Set(UserContext user);
    void Clear();
}
