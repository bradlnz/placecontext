using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PlaceContext.Application.Ports;
using PlaceContext.Infrastructure.Persistence;

namespace PlaceContext.Infrastructure.Workload;

public sealed class TypeSafeConfiguration(IServiceScopeFactory scopes, ICurrentTenant tenant,
    ISecretProtector protector, IOptions<TypeSafeOptions> options)
{
    public async Task<TypeSafeOptions> ResolveAsync(Guid? projectId, CancellationToken ct = default)
    {
        var key = options.Value.ApiKey;
        if (projectId is { } id)
        {
            if (!tenant.IsResolved || tenant.TenantId == Guid.Empty)
                throw new UnauthorizedAccessException("A tenant is required to resolve the project's Jev configuration.");
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (!await db.Projects.AnyAsync(x => x.Id == id, ct)) throw new InvalidOperationException("Project not found.");
            var cipher = await db.JobSecrets.AsNoTracking()
                .Where(x => x.ProjectId == id && x.Name == "TYPESAFE_API_KEY").Select(x => x.Cipher).SingleOrDefaultAsync(ct);
            if (cipher is not null)
            {
                try { key = protector.Unprotect(cipher); }
                catch { throw new InvalidOperationException("The project's TYPESAFE_API_KEY could not be read from the vault."); }
            }
        }
        return new() { ApiKey = key, Model = options.Value.Model };
    }
}
