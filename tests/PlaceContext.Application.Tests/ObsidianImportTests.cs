using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.TestSupport;
using Xunit;

namespace PlaceContext.Application.Tests;

/// <summary>
/// Obsidian import: each note becomes a ledger record whose touched files are the note plus its
/// [[wikilink]] targets — so the existing graph assembler turns the vault's link structure into
/// the project graph, and heavily-linked notes surface as hubs.
/// </summary>
public class ObsidianImportTests
{
    private static readonly DateTimeOffset T0 = new(2026, 7, 9, 14, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("plain [[Target]] link", new[] { "Target" })]
    [InlineData("alias [[Target|shown text]] and heading [[Other#Section]]", new[] { "Target", "Other" })]
    [InlineData("dedupe [[Same]] and [[Same|again]] and [[Same#h]]", new[] { "Same" })]
    [InlineData("embeds ![[image.png]] are skipped, [[Note]] kept", new[] { "Note" })]
    [InlineData("no links here", new string[0])]
    public void Wikilink_parsing_handles_aliases_headings_dedupe_and_embeds(string markdown, string[] expected)
        => Assert.Equal(expected, ObsidianVaultImporter.ParseWikilinks(markdown));

    [Fact]
    public async Task Import_records_one_ledger_entry_per_linked_note_with_link_targets_as_touched_files()
    {
        var projects = new InMemoryProjectRepository();
        var project = Project.Discover(ProjectPath.From("/vaults/demo"), ProjectName.From("demo"), T0);
        await projects.AddAsync(project);
        var ledgers = new InMemoryActivityLogRepository();

        var files = new FakeRepoFiles();
        files.Files["Home.md"] = "Start at [[Projects]] or [[Daily/2026-07-09|today]].";
        files.Files["Projects.md"] = "- [[PlaceContext]]";
        files.Files["Lonely.md"] = "no links at all";
        files.Files[".obsidian/config.md"] = "[[ShouldNotAppear]]"; // vault config is skipped

        var importer = new ObsidianVaultImporter(projects, ledgers, files, new RecordingUnitOfWork(), new FakeClock(T0));
        var result = await importer.ImportAsync(project.Id.Value);

        Assert.Equal(3, result.Notes);        // Home, Projects, Lonely (config skipped)
        Assert.Equal(3, result.Links);        // Projects, Daily/2026-07-09, PlaceContext

        var ledger = await ledgers.GetForProjectAsync(project.Id);
        Assert.Equal(3, ledger.Records.Count);

        var home = ledger.Records.Single(r => r.Summary.Contains("Home"));
        Assert.Contains("Home.md", home.TouchedFiles);
        Assert.Contains("Projects.md", home.TouchedFiles);           // wikilink target → file node
        Assert.Contains("Daily/2026-07-09.md", home.TouchedFiles);   // alias resolved to the target
        Assert.DoesNotContain(ledger.Records, r => r.Summary.Contains("ShouldNotAppear"));
    }

    [Fact]
    public async Task Import_rejects_an_unknown_project()
    {
        var importer = new ObsidianVaultImporter(
            new InMemoryProjectRepository(), new InMemoryActivityLogRepository(),
            new FakeRepoFiles(), new RecordingUnitOfWork(), new FakeClock(T0));

        await Assert.ThrowsAsync<InvalidOperationException>(() => importer.ImportAsync(Guid.NewGuid()));
    }
}
