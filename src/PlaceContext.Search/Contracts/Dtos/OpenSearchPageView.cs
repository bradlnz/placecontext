namespace PlaceContext.Application.Dtos;

public sealed record OpenSearchPageView(
    IReadOnlyList<OpenSearchIndexView> Indices,
    IReadOnlyList<OpenSearchDashboardView> Dashboards,
    string SelectedIndex,
    IReadOnlyList<OpenSearchFieldView> Fields,
    OpenSearchLastUpdatedView? LastUpdated,
    bool CanSync,
    string? Error);
