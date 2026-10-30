namespace PlaceContext.Application.Ports;

public sealed record QemuRunContext(Guid ProjectId, Guid RunId, int Step);
public sealed record QemuLoginProfile(Guid Id, string Name, string ImageProfile, DateTimeOffset UpdatedAt);
public sealed record QemuDesktopStatus(string State, string? Message = null);
public sealed record QemuAgentInteraction(
    Func<CancellationToken, Task<string>> Checkpoint,
    Action RequestControl,
    Func<byte[]?, CancellationToken, Task> SaveProfile);

public interface IQemuDesktop
{
    Task<QemuDesktopStatus?> StatusAsync(QemuRunContext run, CancellationToken ct = default);
    Task TakeControlAsync(QemuRunContext run, CancellationToken ct = default);
    Task ResumeAsync(QemuRunContext run, CancellationToken ct = default);
    Task SaveLoginAsync(QemuRunContext run, string name, CancellationToken ct = default);
    Task<Stream> ConnectAsync(QemuRunContext run, CancellationToken ct = default, bool viewOnly = false);
}

public interface IQemuLoginStore
{
    Task<IReadOnlyList<QemuLoginProfile>> ListAsync(Guid projectId, CancellationToken ct = default);
    Task<byte[]> ReadAsync(Guid projectId, Guid profileId, string imageProfile, CancellationToken ct = default);
    Task SaveAsync(Guid projectId, string name, string imageProfile, byte[] profile, CancellationToken ct = default);
    Task DeleteAsync(Guid projectId, Guid profileId, CancellationToken ct = default);
}
