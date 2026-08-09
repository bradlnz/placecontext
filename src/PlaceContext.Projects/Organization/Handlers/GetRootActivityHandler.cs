using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.Services;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

public sealed class GetRootActivityHandler : IQueryHandler<GetRootActivityQuery, RootActivityView>
{
    private readonly IProjectRepository _projects;
    private readonly IActivityLogRepository _ledgers;

    public GetRootActivityHandler(IProjectRepository projects, IActivityLogRepository ledgers)
    {
        _projects = projects;
        _ledgers = ledgers;
    }

    public async Task<RootActivityView> HandleAsync(GetRootActivityQuery query, CancellationToken ct = default)
    {
        var projects = await _projects.ListAsync(ct);
        var entries = new List<ActivityEntryView>();

        foreach (var p in projects)
        {
            var ledger = await _ledgers.GetForProjectAsync(p.Id, ct);

            foreach (var r in ledger.Records)
            {
                entries.Add(new ActivityEntryView(
                    r.Id.Value, r.Sequence, p.Name.Value,
                    r.Author.Name, r.Author.Kind.ToString(),
                    string.IsNullOrWhiteSpace(r.Summary) ? "(no summary)" : r.Summary,
                    r.Rationale.IsPresent ? r.Rationale.Value : "No rationale recorded for this change.",
                    r.Rationale.IsPresent,
                    FormatTestDelta(r.TestDelta),
                    r.Commit?.Short,
                    r.TouchedFiles.Count,
                    r.TouchedFiles,
                    r.RecordedAt));
            }
        }

        var ordered = entries.OrderByDescending(e => e.RecordedAt).Take(query.Take).ToList();
        return new RootActivityView(ordered);
    }

    private static string FormatTestDelta(TestDelta d)
        => d.HasTestActivity ? $"+{d.Added} / −{d.Removed}" : "none";
}
