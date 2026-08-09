namespace PlaceContext.App.Dashboard;

public sealed record DashboardParameter(string Name, string Label, bool Required, string Type, IReadOnlyList<string> Options, string DefaultValue);
