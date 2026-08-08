namespace PlaceContext.Jobs.Infrastructure.Persistence;

/// <summary>Flat EF Core row for a <see cref="PlaceContext.Domain.Entities.JobChain"/>. Stages are a
/// JSON array of arrays — <c>[["jobA"],["jobB1","jobB2"],["jobC"]]</c> — one element per stage, each
/// holding that stage's job id(s); a stage with more than one id is a parallel fan-out group. A
/// purely linear chain is exactly one job id per stage.</summary>
public sealed class JobChainRow : IJobsTenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string StagesJson { get; set; } = "[]";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
