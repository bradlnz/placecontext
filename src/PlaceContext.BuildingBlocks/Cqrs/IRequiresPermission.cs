namespace PlaceContext.Application.Cqrs;

public interface IRequiresPermission
{
    string RequiredPermission { get; }
}
