namespace PlaceContext.Application.Workload.VirtualMachines;

public sealed record VirtualMachineReplayFrame(int Sequence, long ElapsedMilliseconds, DateTimeOffset CapturedAt,
    string Description, int? ExitCode, bool HasImage, string? Error = null,
    double? PointerX = null, double? PointerY = null, bool Click = false);
