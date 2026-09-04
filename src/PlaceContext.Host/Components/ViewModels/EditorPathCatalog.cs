namespace PlaceContext.Host.Components.ViewModels;

public static class EditorPathCatalog
{
    public static string Normalize(string value) => value.Trim().Replace('\\', '/').TrimStart('/');
}
