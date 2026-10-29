using PlaceContext.Application.Ports;

namespace PlaceContext.TestSupport;

public sealed class FakeQemuReplayStore : IQemuReplayStore
{
    public List<(Guid RunId, int Step, QemuReplayCapture Capture)> Captures { get; } = [];
    public Task AppendAsync(Guid runId, int step, QemuReplayCapture capture, CancellationToken ct)
    { Captures.Add((runId, step, capture)); return Task.CompletedTask; }
    public Task<IReadOnlyList<QemuReplayFrame>> ListAsync(Guid projectId, Guid runId, int step, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<QemuReplayFrame>>(Captures.Where(item => item.RunId == runId && item.Step == step).Select(item => item.Capture.Frame).ToArray());
    public Task<byte[]?> ReadImageAsync(Guid projectId, Guid runId, int step, int sequence, CancellationToken ct) =>
        Task.FromResult(Captures.FirstOrDefault(item => item.RunId == runId && item.Step == step && item.Capture.Frame.Sequence == sequence).Capture?.Png);
}
