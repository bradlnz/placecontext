namespace PlaceContext.Crm.Integration;

public sealed record CrmArtifactContent(string ContentBase64, string ContentType)
{
    public byte[] Content => Convert.FromBase64String(ContentBase64);
}
