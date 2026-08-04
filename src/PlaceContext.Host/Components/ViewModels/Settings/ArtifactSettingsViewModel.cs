using PlaceContext.Application.Ports;
using PlaceContext.Host;

namespace PlaceContext.Host.Components.ViewModels;

public sealed class ArtifactSettingsViewModel(IArtifactViewConfigService config, PortalUiState ui)
    : PageViewModel
{
    public sealed class RuleRow
    {
        public string Id { get; set; } = "";
        public string Label { get; set; } = "";
        public string PrefixText { get; set; } = "";
    }

    public List<RuleRow> Rows { get; } = [];
    public bool Saving { get; private set; }
    public bool Saved { get; private set; }
    public string? Message { get; private set; }

    public async Task LoadAsync()
    {
        ui.Set("Settings", "Artifact filters");
        LoadRows(await config.GetAsync());
        NotifyStateChanged();
    }

    public void AddRule()
    {
        Rows.Add(new RuleRow { Id = $"category-{Guid.NewGuid():N}" });
        NotifyStateChanged();
    }

    public void Remove(RuleRow row)
    {
        Rows.Remove(row);
        NotifyStateChanged();
    }

    public void Move(int index, int delta)
    {
        var target = index + delta;
        if (target < 0 || target >= Rows.Count)
            return;
        (Rows[index], Rows[target]) = (Rows[target], Rows[index]);
        NotifyStateChanged();
    }

    public void Reset()
    {
        LoadRows(config.DefaultConfig());
        Saved = false;
        Message = "Defaults restored locally. Save to apply them.";
        NotifyStateChanged();
    }

    public async Task SaveAsync()
    {
        Saving = true;
        Saved = false;
        Message = null;
        NotifyStateChanged();
        try
        {
            var invalid = Rows.FirstOrDefault(row =>
                string.IsNullOrWhiteSpace(row.Label) || ParsePrefixes(row.PrefixText).Count == 0
            );
            if (invalid is not null)
            {
                Message = "Every filter needs a button label and at least one filename prefix.";
                return;
            }
            await config.SaveAsync(
                new ArtifactViewConfig(
                    Rows.Select(row => new ArtifactCategoryRule(
                            row.Id,
                            row.Label.Trim(),
                            ParsePrefixes(row.PrefixText)
                        ))
                        .ToList()
                )
            );
            LoadRows(await config.GetAsync());
            Saved = true;
            Message = "Artifact filters saved.";
        }
        catch (Exception ex)
        {
            Message = ex.Message;
        }
        finally
        {
            Saving = false;
            NotifyStateChanged();
        }
    }

    public static IReadOnlyList<string> ParsePrefixes(string value) =>
        value
            .Split(
                [',', '\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
            )
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private void LoadRows(ArtifactViewConfig value)
    {
        Rows.Clear();
        Rows.AddRange(
            value.Categories.Select(category => new RuleRow
            {
                Id = category.Id,
                Label = category.Label,
                PrefixText = string.Join(Environment.NewLine, category.Prefixes),
            })
        );
    }
}
