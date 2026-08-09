namespace PlaceContext.Application.Dtos;

/// <summary>Read model: one tenant role definition with its granted permissions and how many members
/// currently hold it — powers the "Roles & permissions" section of the Access settings UI.</summary>
public sealed record RoleView(Guid Id, string Name, bool IsSystem, IReadOnlyList<string> Permissions, int MemberCount);
