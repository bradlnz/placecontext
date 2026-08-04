namespace PlaceContext.Host.Components.ViewModels;

public enum OpenSearchMetricMode
{
    Count,
    Sum,
    Average,
    Minimum,
    Maximum,
}

public enum OpenSearchBucketMode
{
    Terms,
    DateHistogram,
}

public static class OpenSearchPresentationCatalog
{
    public static OpenSearchMetricMode ParseMetric(string value) =>
        value switch
        {
            "sum" => OpenSearchMetricMode.Sum,
            "avg" => OpenSearchMetricMode.Average,
            "min" => OpenSearchMetricMode.Minimum,
            "max" => OpenSearchMetricMode.Maximum,
            _ => OpenSearchMetricMode.Count,
        };

    public static string MetricKey(OpenSearchMetricMode value) =>
        value switch
        {
            OpenSearchMetricMode.Sum => "sum",
            OpenSearchMetricMode.Average => "avg",
            OpenSearchMetricMode.Minimum => "min",
            OpenSearchMetricMode.Maximum => "max",
            _ => "count",
        };

    public static OpenSearchBucketMode ParseBucket(string value) =>
        value == "date_histogram" ? OpenSearchBucketMode.DateHistogram : OpenSearchBucketMode.Terms;

    public static string BucketKey(OpenSearchBucketMode value) =>
        value == OpenSearchBucketMode.DateHistogram ? "date_histogram" : "terms";
}
