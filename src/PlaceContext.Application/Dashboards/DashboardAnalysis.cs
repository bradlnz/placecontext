namespace PlaceContext.Application.Dashboards;

public sealed record DashboardAnalysis(string Question, double Probability, DateTimeOffset AnalyzedAt, string Fingerprint)
{
    public string Verdict => Probability >= .9 ? "Supported by this snapshot" : Probability <= .1 ? "Not supported by this snapshot" : "Uncertain — review the data";
}
