namespace PlaceContext.Host.Components.ViewModels;

public sealed class ContainerLogsViewModel : PageViewModel, IComponentViewModel
{
    private string? _text;
    public string Search { get; set; } = "";
    public string Level { get; set; } = "";
    public bool Wrap { get; set; } = true;
    public IReadOnlyList<JobLogEvent> Events { get; private set; } = [];
    public IEnumerable<JobLogEvent> Filtered => Events.Where(item => (Level.Length == 0 || item.Level == Level)
        && (Search.Length == 0 || item.Message.Contains(Search, StringComparison.OrdinalIgnoreCase)));

    public void SetText(string text)
    {
        if (_text == text)
        {
            return;
        }
        _text = text;
        Events = Parse(text);
    }

    public static IReadOnlyList<JobLogEvent> Parse(string text)
    {
        var events = new List<JobLogEvent>();
        var stream = "stdout";
        foreach (var line in text.Split('\n'))
        {
            if (line == "── stdout ──")
            {
                stream = "stdout";
                continue;
            }
            if (line == "── stderr ──")
            {
                stream = "stderr";
                continue;
            }
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }
            var entry = JobRunLogsViewModel.Parse(events.Count + 1, stream, line.TrimEnd('\r'));
            var space = line.IndexOf(' ');
            if (space > 0 && DateTimeOffset.TryParse(line[..space], System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal, out _))
            {
                entry = entry with
                {
                    Message = line[(space + 1)..].TrimEnd('\r')
                };
            }
            events.Add(entry);
        }
        return events;
    }
}
