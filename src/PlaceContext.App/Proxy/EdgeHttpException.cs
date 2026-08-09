namespace PlaceContext.App.Proxy;

public sealed class EdgeHttpException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
