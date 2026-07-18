using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

/// <summary>What one vault import produced.</summary>
public sealed record ObsidianImportResult(int Notes, int Links);

/// <summary>
/// Turns an extracted Obsidian vault (a folder of markdown notes) into project knowledge: every
/// note becomes one ledger record whose touched files are the note and its [[wikilink]] targets.
/// The existing decision-tree assembler then renders the vault's link structure as the project
/// graph — heavily-linked notes accrue degree and surface as hubs — with no new graph machinery.
/// Run once per import (idempotent enough: re-importing appends a second pass of records).
/// </summary>
public sealed class ObsidianVaultImporter
{
    /// <summary>A vault larger than this still imports, but only the first N notes become records.</summary>
    private const int MaxNotes = 500;

    private readonly IProjectRepository _projects;
    private readonly IActivityLogRepository _ledgers;
    private readonly IRepoFiles _files;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;
    private readonly ILogger<ObsidianVaultImporter>? _log;

    public ObsidianVaultImporter(IProjectRepository projects, IActivityLogRepository ledgers,
        IRepoFiles files, IUnitOfWork uow, IClock clock, ILogger<ObsidianVaultImporter>? log = null)
    {
        _projects = projects;
        _ledgers = ledgers;
        _files = files;
        _uow = uow;
        _clock = clock;
        _log = log;
    }

    // [[Target]], [[Target|alias]], [[Target#heading]] — group 1 is the note name. A leading '!'
    // is an embed (image/attachment transclusion), not a knowledge link, so it's excluded.
    private static readonly Regex WikilinkRe = new(
        @"(?<!\!)\[\[([^\]\|#\r\n]+)(?:#[^\]\|]*)?(?:\|[^\]]*)?\]\]", RegexOptions.Compiled);

    /// <summary>Distinct wikilink targets in one note, in order of first appearance.</summary>
    public static IReadOnlyList<string> ParseWikilinks(string markdown)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var targets = new List<string>();
        foreach (Match m in WikilinkRe.Matches(markdown))
        {
            var target = m.Groups[1].Value.Trim();
            if (target.Length > 0 && seen.Add(target))
                targets.Add(target);
        }
        return targets;
    }

    public async Task<ObsidianImportResult> ImportAsync(Guid projectId, CancellationToken ct = default)
    {
        var pid = ProjectId.From(projectId);
        var project = await _projects.GetByIdAsync(pid, ct)
            ?? throw new InvalidOperationException($"Project {projectId} not found.");

        var notes = await _files.ListAsync(project.Path, ".md", ct);
        if (notes.Count > MaxNotes)
        {
            _log?.LogWarning("Obsidian import: vault has {Count} notes; importing the first {Max}.", notes.Count, MaxNotes);
            notes = notes.Take(MaxNotes).ToList();
        }

        var ledger = await _ledgers.GetForProjectAsync(pid, ct);
        var author = Author.From("obsidian-import", ActorKind.Agent);
        var imported = 0;
        var links = 0;

        foreach (var note in notes)
        {
            ct.ThrowIfCancellationRequested();
            var markdown = await _files.ReadAsync(project.Path, note, ct);
            if (markdown is null) continue;

            var targets = ParseWikilinks(markdown);
            links += targets.Count;

            // The note plus its link targets — shared targets accrue degree across records, which is
            // exactly how the assembler surfaces hubs. Targets get ".md" so they merge with the
            // target note's own file node.
            var touched = new List<string> { note };
            touched.AddRange(targets.Select(t => t.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ? t : t + ".md"));

            ledger.Append(
                $"note: {NoteTitle(note)}",
                author,
                Rationale.OrNone(null),
                TestDelta.From(0, 0, 0),
                new ActivityVerification(false, false),
                touched.Distinct(StringComparer.OrdinalIgnoreCase),
                Array.Empty<GraphNodeId>(),
                _clock.UtcNow);
            imported++;
        }

        await _ledgers.SaveAsync(ledger, ct);
        await _uow.SaveChangesAsync(ct);
        _log?.LogInformation("Obsidian import for {Project}: {Notes} note(s), {Links} wikilink(s).",
            project.Name, imported, links);
        return new ObsidianImportResult(imported, links);
    }

    private static string NoteTitle(string relativePath)
    {
        var name = relativePath.Replace('\\', '/');
        var slash = name.LastIndexOf('/');
        if (slash >= 0) name = name[(slash + 1)..];
        return name.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ? name[..^3] : name;
    }
}
