namespace PlaceContext.Application.Dashboards;

public sealed record DashboardSeriesProfile(
    string Name, int ValueCount, double Minimum, double Maximum,
    string MinimumLabel, string MaximumLabel, int ZeroCount, int NegativeCount);
