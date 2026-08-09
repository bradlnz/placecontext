namespace PlaceContext.Application.Ports;

public interface ICurrentUserAccessor
{
    void Set(UserContext user);
    void Clear();
}
