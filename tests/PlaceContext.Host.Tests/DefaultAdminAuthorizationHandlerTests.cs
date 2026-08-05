using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PlaceContext.Application.Ports;
using PlaceContext.Host.Auth;
using PlaceContext.Infrastructure.Persistence;

namespace PlaceContext.Host.Tests;

/// <summary>The DefaultAdmin policy handler: succeeds only for the user row flagged IsDefaultAdmin,
/// deny-by-default for everyone else (including a missing/unparsable identity).</summary>
public sealed class DefaultAdminAuthorizationHandlerTests
{
    private static (DefaultAdminAuthorizationHandler Handler, AppDbContext Db) NewHandler()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentTenant>(new StubTenant());
        // The database name must be hoisted out of the options lambda — it runs per context instance,
        // so an inline Guid would give the handler's isolated scope its own empty InMemory database.
        var dbName = Guid.NewGuid().ToString("N");
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(dbName));
        var provider = services.BuildServiceProvider();
        var db = provider.GetRequiredService<AppDbContext>();
        return (
            new DefaultAdminAuthorizationHandler(
                provider.GetRequiredService<IServiceScopeFactory>()
            ),
            db
        );
    }

    private static async Task<bool> Succeeds(
        DefaultAdminAuthorizationHandler handler,
        ClaimsPrincipal user
    )
    {
        var context = new AuthorizationHandlerContext(
            new[] { new DefaultAdminRequirement() },
            user,
            resource: null
        );
        await handler.HandleAsync(context);
        return context.HasSucceeded;
    }

    private static ClaimsPrincipal Principal(Guid userId) =>
        new(
            new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) },
                "test"
            )
        );

    private async Task<Guid> AddUser(AppDbContext db, bool isDefaultAdmin)
    {
        var row = new UserRow
        {
            Id = Guid.NewGuid(),
            Email = $"{Guid.NewGuid():N}@example.com",
            DisplayName = "Member",
            PasswordHash = "x",
            PasswordSet = true,
            Role = "Owner",
            IsDefaultAdmin = isDefaultAdmin,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Users.Add(row);
        await db.SaveChangesAsync();
        return row.Id;
    }

    [Fact]
    public async Task The_default_admin_satisfies_the_requirement()
    {
        var (handler, db) = NewHandler();
        var userId = await AddUser(db, isDefaultAdmin: true);

        Assert.True(await Succeeds(handler, Principal(userId)));
    }

    [Fact]
    public async Task An_ordinary_user_is_denied()
    {
        var (handler, db) = NewHandler();
        var userId = await AddUser(db, isDefaultAdmin: false);

        Assert.False(await Succeeds(handler, Principal(userId)));
    }

    [Fact]
    public async Task An_unknown_user_is_denied()
    {
        var (handler, _) = NewHandler();

        Assert.False(await Succeeds(handler, Principal(Guid.NewGuid())));
    }

    [Fact]
    public async Task A_principal_without_a_parseable_identity_is_denied()
    {
        var (handler, _) = NewHandler();

        Assert.False(await Succeeds(handler, new ClaimsPrincipal(new ClaimsIdentity())));
    }

    private sealed class StubTenant : ICurrentTenant
    {
        public Guid TenantId => Guid.Empty;
        public string Slug => string.Empty;
        public string TimeZoneId => "UTC";
        public bool IsResolved => false;
    }
}
