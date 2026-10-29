namespace PlaceContext.Application.Ports;
public sealed record QemuReplayCapture(QemuReplayFrame Frame, byte[]? Png);
