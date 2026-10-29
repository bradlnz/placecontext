namespace PlaceContext.Application.Ports;

public sealed record QemuReplayFrame(int Sequence, long ElapsedMilliseconds, DateTimeOffset CapturedAt,
    string Description, int? ExitCode, bool HasImage, string? Error = null);
