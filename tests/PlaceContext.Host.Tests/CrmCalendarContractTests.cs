namespace PlaceContext.Host.Tests;

public sealed class CrmCalendarContractTests
{
    [Fact]
    public void Calendar_catalogue_opens_month_tiles_and_supports_event_editing()
    {
        var root = FindRepoRoot();
        var page = File.ReadAllText(Path.Combine(root, "src", "PlaceContext.Host", "Components", "Pages", "Crm.razor"));

        Assert.Contains("Calendar catalogue", page, StringComparison.Ordinal);
        Assert.Contains("class=\"calendar-grid\"", page, StringComparison.Ordinal);
        Assert.Contains("@foreach (var day in CalendarDays)", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Appointment creation will be enabled", page, StringComparison.Ordinal);
        Assert.Contains("CreateCrmAppointmentAsync", page, StringComparison.Ordinal);
        Assert.Contains("DeleteCrmAppointmentAsync", page, StringComparison.Ordinal);
        Assert.Contains("SaveCrmCalendarAsync", page, StringComparison.Ordinal);
        Assert.Contains("DeleteCrmCalendarAsync", page, StringComparison.Ordinal);
        Assert.Contains("ListCrmAppointmentsAsync", page, StringComparison.Ordinal);
        Assert.Contains("ListCrmCalendarsAsync", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Lifecycle_overview_has_internal_padding()
    {
        var root = FindRepoRoot();
        var css = File.ReadAllText(Path.Combine(root, "src", "PlaceContext.Host", "Components", "Pages", "Crm.razor.css"));
        Assert.Contains(".lifecycle-panel {\n    padding: 4px;", css, StringComparison.Ordinal);
        Assert.Contains(".lifecycle-step:not(:last-child)::after {", css, StringComparison.Ordinal);
        Assert.Contains("column-gap: 12px;", css, StringComparison.Ordinal);
        Assert.Contains("right: -10px;", css, StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "src", "PlaceContext.Host"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Repository root not found.");
    }
}
