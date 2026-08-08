using Microsoft.EntityFrameworkCore;
using PlaceContext.Data.Infrastructure.Persistence;
using PlaceContext.TestSupport;

namespace PlaceContext.Data.Tests;

public sealed class DataDbContextTests
{
    [Fact]
    public async Task SaveChangesAsync_UnstampedRows_StampsTenantAndIsolatesReads()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var tenantId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var options = new DbContextOptionsBuilder<DataDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

        await using (var owner = new DataDbContext(options, new FakeCurrentTenant(tenantId)))
        {
            owner.ProjectCharts.Add(new ProjectChartRow
                { Id = Guid.NewGuid(), ProjectId = projectId, TableName = "sales", Html = "{}", GeneratedAt = now });
            owner.DataMappings.Add(new DataMappingRow
                { Id = Guid.NewGuid(), ProjectId = projectId, JobId = Guid.NewGuid(), TargetTable = "sales", CreatedAt = now, UpdatedAt = now });
            owner.DataEntities.Add(new DataEntityRow
                { Id = Guid.NewGuid(), ProjectId = projectId, Name = "Sale", TableName = "sales", CreatedAt = now, UpdatedAt = now });
            owner.EntityTags.Add(new EntityTagRow
                { Id = Guid.NewGuid(), ProjectId = projectId, EntityId = Guid.NewGuid(), EntityName = "Sale", Key = "1", RunId = Guid.NewGuid(), JobId = Guid.NewGuid(), CreatedAt = now });
            owner.RecordLinks.Add(new RecordLinkRow
                { Id = Guid.NewGuid(), ProjectId = projectId, Kind = "email", NormalizedValue = "a@example.com", DisplayValue = "a@example.com", TableName = "sales", ColumnName = "email", RowKey = "1", CreatedAt = now });
            owner.SavedQueries.Add(new SavedQueryRow
                { Id = Guid.NewGuid(), ProjectId = projectId, Name = "Recent", Sql = "select 1", CreatedAt = now, UpdatedAt = now });

            await owner.SaveChangesAsync();

            Assert.All(owner.ChangeTracker.Entries<IDataTenantOwned>(), entry =>
                Assert.Equal(tenantId, entry.Entity.TenantId));
        }

        await using var otherTenant = new DataDbContext(options, new FakeCurrentTenant(Guid.NewGuid()));
        Assert.Empty(await otherTenant.ProjectCharts.ToListAsync());
        Assert.Empty(await otherTenant.DataMappings.ToListAsync());
        Assert.Empty(await otherTenant.DataEntities.ToListAsync());
        Assert.Empty(await otherTenant.EntityTags.ToListAsync());
        Assert.Empty(await otherTenant.RecordLinks.ToListAsync());
        Assert.Empty(await otherTenant.SavedQueries.ToListAsync());
    }
}
