namespace PlaceContext.Host.Components.ViewModels;

public static class SearchPresentationCatalog
{
    public static SearchMetricMode ParseMetric(string value) =>
        value switch
        {
            "sum" => SearchMetricMode.Sum,
            "avg" => SearchMetricMode.Average,
            "min" => SearchMetricMode.Minimum,
            "max" => SearchMetricMode.Maximum,
            _ => SearchMetricMode.Count,
        };

    public static string MetricKey(SearchMetricMode value) =>
        value switch
        {
            SearchMetricMode.Sum => "sum",
            SearchMetricMode.Average => "avg",
            SearchMetricMode.Minimum => "min",
            SearchMetricMode.Maximum => "max",
            _ => "count",
        };

    public static SearchBucketMode ParseBucket(string value) =>
        value == "date_histogram" ? SearchBucketMode.DateHistogram : SearchBucketMode.Terms;

    public static string BucketKey(SearchBucketMode value) =>
        value == SearchBucketMode.DateHistogram ? "date_histogram" : "terms";
}
