namespace PlaceContext.Application.Features;

public sealed record PostJobBuiltFile(
    string FileName,
    byte[] Content,
    string ContentType,
    string Title);
