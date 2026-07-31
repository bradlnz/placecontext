using Microsoft.EntityFrameworkCore;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;

namespace PlaceContext.Infrastructure.Persistence;

public sealed class EfJobTestStore : IJobTestStore
{
    private readonly AppDbContext _db;
    public EfJobTestStore(AppDbContext db) => _db = db;

    public async Task<JobTestCaseRecord?> GetAsync(Guid id, CancellationToken ct = default)
        => ToRecord(await _db.JobTestCases.AsNoTracking()
            .FirstOrDefaultAsync(test => test.Id == id, ct));

    public async Task<IReadOnlyList<JobTestCaseRecord>> ListForProjectAsync(
        Guid projectId, CancellationToken ct = default)
        => (await _db.JobTestCases.AsNoTracking()
                .Where(test => test.ProjectId == projectId)
                .OrderBy(test => test.JobId)
                .ThenBy(test => test.Name)
                .ToListAsync(ct))
            .Select(row => ToRecord(row)!)
            .ToList();

    public async Task SaveAsync(JobTestCaseRecord test, CancellationToken ct = default)
    {
        var row = await _db.JobTestCases.FirstOrDefaultAsync(existing => existing.Id == test.Id, ct);
        if (row is null)
        {
            row = new JobTestCaseRow { Id = test.Id };
            _db.JobTestCases.Add(row);
        }

        row.ProjectId = test.ProjectId;
        row.JobId = test.JobId;
        row.Name = test.Name;
        row.InputPayload = test.InputPayload;
        row.AssertionType = test.AssertionType.ToString();
        row.ExpectedValue = test.ExpectedValue;
        row.Enabled = test.Enabled;
        row.LastStatus = test.LastStatus;
        row.LastMessage = test.LastMessage;
        row.LastActualOutput = test.LastActualOutput;
        row.LastJobRunId = test.LastJobRunId;
        row.LastRunAt = test.LastRunAt;
        row.LastDurationMs = test.LastDurationMs;
        row.CreatedAt = test.CreatedAt;
        row.UpdatedAt = test.UpdatedAt;
        await _db.SaveChangesAsync(ct);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var row = await _db.JobTestCases.FirstOrDefaultAsync(test => test.Id == id, ct);
        if (row is null) return false;
        _db.JobTestCases.Remove(row);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    private static JobTestCaseRecord? ToRecord(JobTestCaseRow? row)
    {
        if (row is null) return null;
        return new JobTestCaseRecord(
            row.Id,
            row.ProjectId,
            row.JobId,
            row.Name,
            row.InputPayload,
            Enum.TryParse<JobTestAssertionType>(row.AssertionType, out var assertion)
                ? assertion
                : JobTestAssertionType.Succeeds,
            row.ExpectedValue,
            row.Enabled,
            row.LastStatus,
            row.LastMessage,
            row.LastActualOutput,
            row.LastJobRunId,
            row.LastRunAt,
            row.LastDurationMs,
            row.CreatedAt,
            row.UpdatedAt);
    }
}
