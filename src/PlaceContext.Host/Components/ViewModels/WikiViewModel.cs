using PlaceContext.App.Wiki;

namespace PlaceContext.Host.Components.ViewModels;

public sealed class WikiViewModel(PortalUiState ui) : PageViewModel
{
    public IReadOnlyList<WikiArticle> Articles => WikiLibrary.Articles;
    public WikiArticle? Article { get; private set; }
    public bool TocOpen { get; private set; }

    public string ArticleRoute(WikiArticle article) => PageRoutes.WikiArticle(article.Slug);

    public void SetParameters(string? slug)
    {
        Article = slug is null ? WikiLibrary.Articles.FirstOrDefault() : WikiLibrary.Find(slug);
        TocOpen = false;
        ui.Set("Wiki", Article?.Title ?? "platform documentation");
    }

    public void ToggleContents()
    {
        TocOpen = !TocOpen;
        NotifyStateChanged();
    }

    public void CloseContents()
    {
        TocOpen = false;
        NotifyStateChanged();
    }
}
