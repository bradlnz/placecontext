using PlaceContext.Application.Ports;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Design;

namespace PlaceContext.Infrastructure.Persistence;

/// <summary>
/// Design-time factory so the EF tools (<c>dotnet ef migrations …</c>) can build the model without the
/// full app host. Uses a no-op tenant — tenant scoping only matters at runtime, not for generating DDL.
/// </summary>
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Port=5433;Database=placecontext;Username=postgres;Password=postgres")
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;
        return new AppDbContext(options, new DesignTimeTenant());
    }

    private sealed class DesignTimeTenant : ICurrentTenant
    {
        public Guid TenantId => Guid.Empty;
        public string Slug => string.Empty;
        public string TimeZoneId => "UTC";
        public bool IsResolved => false;
    }
}
