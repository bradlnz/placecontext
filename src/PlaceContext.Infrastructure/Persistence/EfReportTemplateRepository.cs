using System.Text.Json;
using System.Text.Json.Serialization;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace PlaceContext.Infrastructure.Persistence;

public sealed class EfReportTemplateRepository : IReportTemplateRepository
{
    private static readonly JsonSerializerOptions Json = new() { Converters = { new JsonStringEnumConverter() } };

    private readonly AppDbContext _db;
    public EfReportTemplateRepository(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<ReportTemplate>> ListAsync(CancellationToken ct = default)
    {
        var rows = await _db.ReportTemplates.AsNoTracking().OrderBy(x => x.Name).ToListAsync(ct);
        return rows.Select(ToDomain).ToList();
    }

    public async Task<ReportTemplate?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var r = await _db.ReportTemplates.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return r is null ? null : ToDomain(r);
    }

    public async Task<ReportTemplate?> GetByNameAsync(string name, CancellationToken ct = default)
    {
        var r = await _db.ReportTemplates.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Name.ToLower() == name.ToLower(), ct);
        return r is null ? null : ToDomain(r);
    }

    public async Task SaveAsync(ReportTemplate template, CancellationToken ct = default)
    {
        var row = await _db.ReportTemplates.FirstOrDefaultAsync(x => x.Id == template.Id, ct);
        var sectionsJson = JsonSerializer.Serialize(template.Sections.Select(SectionDto.From), Json);
        if (row is null)
        {
            await _db.ReportTemplates.AddAsync(new ReportTemplateRow
            {
                Id = template.Id, Name = template.Name, Description = template.Description,
                Sections = sectionsJson, CreatedAt = template.CreatedAt, UpdatedAt = template.UpdatedAt
            }, ct);
        }
        else
        {
            row.Name = template.Name;
            row.Description = template.Description;
            row.Sections = sectionsJson;
            row.UpdatedAt = template.UpdatedAt;
        }
    }

    private static ReportTemplate ToDomain(ReportTemplateRow r)
    {
        var sections = (JsonSerializer.Deserialize<List<SectionDto>>(r.Sections, Json) ?? new())
            .Select(s => new ReportSection(s.Title, s.Source, s.Instruction));
        return ReportTemplate.Rehydrate(r.Id, r.Name, r.Description, sections, r.CreatedAt, r.UpdatedAt);
    }

    private sealed record SectionDto(string Title, ReportSourceKind Source, string? Instruction)
    {
        public static SectionDto From(ReportSection s) => new(s.Title, s.Source, s.Instruction);
    }
}
