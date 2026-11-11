using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PlaceContext.Application;
using PlaceContext.Application.Access;
using PlaceContext.Application.Access.Models;
using PlaceContext.Application.Auth;
using PlaceContext.Application.Communications;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Features;
using PlaceContext.Application.Membership;
using PlaceContext.Application.Mcp;
using PlaceContext.Application.ProjectData;
using PlaceContext.Application.Secrets;
using PlaceContext.Application.Tenancy;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.Entities;
using PlaceContext.Host.Auth;
using PlaceContext.Host.Controllers;
using PlaceContext.Host.Tenancy;
using PlaceContext.Host.Components.ViewModels;
using PlaceContext.Infrastructure.Auth;
using PlaceContext.Infrastructure.Comms;
using PlaceContext.Infrastructure.Persistence;
using PlaceContext.TestSupport;

namespace PlaceContext.Host.Tests;

public sealed class SecurityBoundaryTests
{
    [Fact]
    public async Task Cookie_replay_on_another_workspace_and_deleted_members_are_rejected()
    {
        using var f = new Fixture();
        var origin = f.Http();
        await origin.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, f.Principal());
        var cookie = origin.Response.Headers.SetCookie.Single(x => x!.StartsWith(".AspNetCore.Cookies="))!.Split(';')[0];
        var ownTenant = f.Tenant.TenantId;
        async Task<AuthenticateResult> Authenticate()
        {
            using var scope = f.Services.CreateScope();
            var request = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
            request.Request.Headers.Cookie = cookie;
            return await request.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }
        Assert.True((await Authenticate()).Succeeded);
        f.Tenant.TenantId = Guid.NewGuid();
        Assert.False((await Authenticate()).Succeeded);
        f.Tenant.TenantId = ownTenant;
        f.Db.Users.Remove(f.User);
        await f.Db.SaveChangesAsync();
        Assert.False((await Authenticate()).Succeeded);
    }

    [Fact]
    public async Task Administrator_policy_and_permissions_do_not_accept_a_foreign_or_missing_account()
    {
        using var f = new Fixture();
        var principal = f.Principal();
        f.Tenant.TenantId = Guid.NewGuid();
        var context = new AuthorizationHandlerContext([new DefaultAdminRequirement()], principal, null);
        await new DefaultAdminAuthorizationHandler(f.Services.GetRequiredService<IServiceScopeFactory>()).HandleAsync(context);
        Assert.False(context.HasSucceeded);
        var permissions = new PermissionService(new CurrentUser(), f.Services.GetRequiredService<IServiceScopeFactory>());
        Assert.Empty(await permissions.GetEffectivePermissionsForUserAsync(f.User.Id, "Owner"));
        Assert.Contains(Permission.JobsEdit, await permissions.GetEffectivePermissionsForUserAsync(TrustedServicePrincipals.ManagementApiUserId, "Owner"));
    }

    [Fact]
    public async Task Session_validation_refreshes_roles_and_rejects_a_deleted_user()
    {
        using var f = new Fixture();
        var principal = f.Principal();
        f.User.Role = "Viewer";
        await f.Db.SaveChangesAsync();
        var members = f.Services.GetRequiredService<IMembershipService>();
        Assert.True(await PortalSession.ValidateAsync(principal, f.Tenant, members));
        Assert.Equal("Viewer", principal.FindFirstValue(ClaimTypes.Role));
        Assert.Equal("Viewer", principal.FindFirstValue("role"));
        f.Db.Users.Remove(f.User);
        await f.Db.SaveChangesAsync();
        Assert.False(await PortalSession.ValidateAsync(principal, f.Tenant, members));
    }

    [Fact]
    public async Task An_open_circuit_stops_dispatching_events_after_membership_is_removed()
    {
        using var f = new Fixture();
        var authentication = new CircuitAuthentication(f.Principal());
        var holder = new UserHolder();
        var handler = new UserCircuitHandler(holder,
            new TenantHolder { Tenant = new TenantInfo(f.Tenant.TenantId, "test", "Test", "UTC") },
            f.Services.GetRequiredService<IServiceScopeFactory>(), authentication);
        var calls = 0;
        var activity = handler.CreateInboundActivityHandler(_ => { calls++; return Task.CompletedTask; });
        await activity(null!);
        Assert.Equal(1, calls);
        f.Db.Users.Remove(f.User);
        await f.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => activity(null!));
        Assert.Equal(1, calls);
        Assert.Null(holder.User);
        Assert.False((await authentication.GetAuthenticationStateAsync()).User.Identity!.IsAuthenticated);
    }

    [Theory]
    [InlineData("Owner")]
    [InlineData("Viewer")]
    public async Task Overlapping_circuit_activities_do_not_mutate_the_shared_authentication_identity(string role)
    {
        using var f = new Fixture();
        var principal = f.Principal();
        var originalClaims = principal.Claims.ToArray();
        var authentication = new CircuitAuthentication(principal);
        var roles = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var members = Stub.Create<IMembershipService>((method, _) => method == nameof(IMembershipService.GetRoleAsync)
            ? roles.Task : throw new NotSupportedException(method));
        using var services = new ServiceCollection().AddSingleton(members).BuildServiceProvider();
        var holder = new UserHolder();
        var handler = new UserCircuitHandler(holder,
            new TenantHolder { Tenant = new TenantInfo(f.Tenant.TenantId, "test", "Test", "UTC") },
            services.GetRequiredService<IServiceScopeFactory>(), authentication);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var activity = handler.CreateInboundActivityHandler(async _ =>
        {
            Assert.Equal(new UserIdentity(f.User.Id, role), CurrentUser.Current);
            if (Interlocked.Increment(ref calls) == 2)
            {
                entered.SetResult();
            }
            await release.Task;
        });
        var first = activity(null!);
        var second = activity(null!);
        roles.SetResult(role);
        try
        {
            // JS completions must enter while another activity awaits them.
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            release.SetResult();
        }
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(originalClaims, principal.Claims.ToArray());
        Assert.Equal("Owner", principal.FindFirstValue(ClaimTypes.Role));
        Assert.Equal(role, holder.User!.Role);
        var current = (await authentication.GetAuthenticationStateAsync()).User;
        Assert.Equal(role, current.FindFirstValue(ClaimTypes.Role));
        if (role == "Owner")
        {
            Assert.Same(principal, current);
        }
        else
        {
            Assert.NotSame(principal, current);
            Assert.Equal(role, current.FindFirstValue("role"));
        }
    }

    [Theory]
    [InlineData("plaintext")]
    [InlineData("tampered")]
    [InlineData("expired")]
    [InlineData("other-tenant")]
    [InlineData("other-browser")]
    public async Task Mfa_rejects_invalid_state_before_changing_a_factor(string kind)
    {
        using var f = new Fixture();
        var controller = f.Controller();
        var payload = f.StateJson(kind == "other-tenant" ? Guid.NewGuid() : null);
        var protector = f.Protection.CreateProtector(AuthController.MfaPurpose).ToTimeLimitedDataProtector();
        var state = kind switch
        {
            "plaintext" => payload,
            "expired" => protector.Protect(payload, DateTimeOffset.UtcNow.AddMinutes(-1)),
            "tampered" => protector.Protect(payload, TimeSpan.FromMinutes(10)) + "tampered",
            _ => protector.Protect(payload, TimeSpan.FromMinutes(10))
        };
        if (kind == "other-browser") controller.Request.Headers.Cookie = "";
        var result = Assert.IsType<RedirectResult>(await controller.EmailVerifyPost(state, null, "+15555550200"));
        Assert.Equal("/login", result.Url);
        Assert.Equal("+15555550100", f.User.PhoneNumber);
        Assert.Null(f.Sms);
        Assert.Null(f.SignedIn);
    }

    [Fact]
    public async Task Password_login_issues_a_browser_bound_challenge_before_signing_in()
    {
        using var f = new Fixture();
        f.User.PasswordHash = PasswordHasher.Hash("Synthetic-password-123!");
        await f.Db.SaveChangesAsync();
        var login = f.Controller();
        var result = Assert.IsType<RedirectResult>(await login.Login(f.User.Email, "Synthetic-password-123!", "/projects"));
        Assert.StartsWith("/auth/email-verify?state=", result.Url);
        Assert.Null(f.SignedIn);
        var cookie = Assert.Single(login.Response.Headers.SetCookie)!.Split(';')[0];
        var verify = f.Controller();
        verify.Request.Headers.Cookie = cookie;
        var state = Uri.UnescapeDataString(result.Url!.Split("state=")[1]);
        var completed = Assert.IsType<RedirectResult>(await verify.EmailVerifyPost(state,
            Regex.Match(f.Sms!, @"\b\d{6}\b").Value, null));
        Assert.Equal("/projects", completed.Url);
        Assert.NotNull(f.SignedIn);
    }

    [Fact]
    public async Task Valid_pending_login_cannot_replace_an_existing_phone()
    {
        using var f = new Fixture();
        Assert.IsType<BadRequestObjectResult>(await f.Controller().EmailVerifyPost(f.State(), null, "+15555550200"));
        Assert.Equal("+15555550100", f.User.PhoneNumber);
        Assert.Null(f.Sms);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Auth.SetTwoFactorPhoneNumberAsync(
            f.User.Id, "+15555550200", onlyIfMissing: true));
    }

    [Fact]
    public async Task Valid_mfa_code_signs_in_with_the_current_database_role_and_cannot_be_reused()
    {
        using var f = new Fixture();
        var state = f.State();
        await f.Auth.IssueTwoFactorCodeAsync(f.User.Id, "sms");
        var code = Regex.Match(f.Sms!, @"\b\d{6}\b").Value;
        f.User.Role = "Viewer";
        await f.Db.SaveChangesAsync();
        await f.Controller().EmailVerifyPost(state, code, null);
        Assert.Equal(f.User.Id.ToString(), f.SignedIn!.FindFirstValue(ClaimTypes.NameIdentifier));
        Assert.Equal("Viewer", f.SignedIn!.FindFirstValue(ClaimTypes.Role));
        f.SignedIn = null;
        await f.Controller().EmailVerifyPost(state, code, null);
        Assert.Null(f.SignedIn);
    }

    [Fact]
    public async Task First_phone_enrollment_still_requires_a_code_and_cannot_replace_it_later()
    {
        using var f = new Fixture();
        f.User.PhoneNumber = null;
        await f.Db.SaveChangesAsync();
        var result = Assert.IsType<RedirectResult>(await f.Controller().EmailVerifyPost(f.State(needsPhone: true), null, "+15555550200"));
        Assert.Equal("+15555550200", f.User.PhoneNumber);
        Assert.NotNull(f.Sms);
        Assert.Null(f.SignedIn);
        var state = Uri.UnescapeDataString(result.Url!.Split("state=")[1]);
        await f.Controller().EmailVerifyPost(state, Regex.Match(f.Sms!, @"\b\d{6}\b").Value, null);
        Assert.NotNull(f.SignedIn);
    }

    [Fact]
    public async Task Connector_endpoints_require_secret_permission_and_do_not_offer_a_raw_token_route()
    {
        var authorization = typeof(McpOAuthController).GetCustomAttribute<AuthorizeAttribute>()!;
        Assert.Equal(Permission.SecretsManage, authorization.Policy);
        using var f = new Fixture();
        f.User.Role = "Viewer";
        f.User.IsDefaultAdmin = false;
        await f.Db.SaveChangesAsync();
        var permissions = new PermissionService(new CurrentUser(), f.Services.GetRequiredService<IServiceScopeFactory>());
        var context = new AuthorizationHandlerContext([new PermissionRequirement(authorization.Policy!)], f.Principal(), null);
        await new PermissionAuthorizationHandler(permissions).HandleAsync(context);
        Assert.False(context.HasSucceeded);
        Assert.DoesNotContain(typeof(McpOAuthController).GetMethods(), method =>
            method.GetCustomAttributes<HttpGetAttribute>().Any(route => route.Template == "/mcp-oauth/token"));
    }

    [Fact]
    public async Task Connector_refresh_keeps_the_new_access_and_refresh_tokens_on_the_server()
    {
        var connection = McpConnection.Create(Guid.NewGuid(), "Test", "http", "https://provider.test/mcp",
            null, null, "oauth", null, null, DateTimeOffset.UtcNow);
        connection.StoreOAuthTokens("old", "secret-refresh", DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow);
        var repository = Stub.Create<IMcpConnectionRepository>((method, _) => method switch {
            "GetByIdAsync" => Task.FromResult<McpConnection?>(connection),
            "UpdateAsync" => Task.CompletedTask,
            _ => throw new NotSupportedException(method)
        });
        var client = Stub.Create<IMcpOAuthClient>((method, _) => method switch {
            "DiscoverMetadataAsync" => Task.FromResult<OAuthMetadata?>(new() { TokenEndpoint = "https://provider.test/token" }),
            "RefreshTokenAsync" => Task.FromResult(new TokenResponse { AccessToken = "secret-access", RefreshToken = "secret-refresh", ExpiresIn = 3600 }),
            _ => throw new NotSupportedException(method)
        });
        var service = new McpOAuthService(repository, new RecordingUnitOfWork(), new FakeDataEncryptor(), client,
            new FakeClock(DateTimeOffset.UtcNow));
        var controller = new McpOAuthController(service, new EphemeralDataProtectionProvider(), NullLogger<McpOAuthController>.Instance)
            { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        var result = Assert.IsType<OkObjectResult>(await controller.Refresh(connection.Id));
        var response = JsonSerializer.SerializeToElement(result.Value);
        Assert.True(response.GetProperty("Connected").GetBoolean());
        Assert.True(response.GetProperty("ExpiresAt").GetDateTimeOffset() > DateTimeOffset.UtcNow);
        Assert.DoesNotContain("secret-", response.ToString());
        Assert.Equal("secret-access", connection.OAuthAccessToken);
        Assert.NotNull(typeof(McpOAuthController).GetMethod(nameof(McpOAuthController.Refresh))!
            .GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
    }

    [Fact]
    public async Task Sql_editor_uses_the_read_only_path_after_write_permission_is_revoked()
    {
        var canWrite = true;
        var permissions = Stub.Create<IPermissionService>((method, args) => {
            Assert.Equal("HasAsync", method);
            Assert.Equal(Permission.DataWrite, args[0]);
            return Task.FromResult(canWrite);
        });
        var calls = new List<object>();
        var dispatcher = Stub.Create<IDispatcher>((_, args) => {
            calls.Add(args[0]!);
            return Task.FromResult(new ProjectQueryResult(["value"], [["1"]], 0, false));
        });
        var vm = new ProjectDataViewModel(new PlaceContextService(dispatcher, null!, null!, null!),
            new PortalUiState(), null!, dispatcher, permissions);
        vm.Initialize(Guid.NewGuid());
        await vm.RunSqlAsync("INSERT INTO records VALUES (1)");
        Assert.IsType<ExecuteProjectDataCommand>(calls[0]);
        canWrite = false;
        await vm.RunSqlAsync("SELECT * FROM records");
        Assert.IsType<QueryProjectDataQuery>(calls[1]);
    }

    [Fact]
    public async Task Sql_write_commands_are_denied_before_their_handlers_run()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var permissions = Stub.Create<IPermissionService>((method, _) => method == "HasAsync"
            ? Task.FromResult(false) : throw new NotSupportedException(method));
        var dispatcher = new Dispatcher(services.GetRequiredService<IServiceScopeFactory>(), permissions);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => dispatcher.Send(new ExecuteProjectDataCommand(Guid.NewGuid(), "DELETE FROM records")));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => dispatcher.Send(new DropProjectTableCommand(Guid.NewGuid(), "records")));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => dispatcher.Send(new CreateProjectTableCommand(Guid.NewGuid(), "records", [])));
    }

    private sealed class Fixture : IDisposable
    {
        public MutableTenant Tenant { get; } = new();
        public IDataProtectionProvider Protection { get; } = new EphemeralDataProtectionProvider();
        public ServiceProvider Services { get; }
        public AppDbContext Db { get; }
        public UserRow User { get; }
        public AuthService Auth { get; }
        public string? Sms;
        public ClaimsPrincipal? SignedIn;
        public Fixture()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<ICurrentTenant>(Tenant);
            services.AddSingleton<ICurrentUser, CurrentUser>();
            services.AddSingleton(Protection);
            var database = Guid.NewGuid().ToString();
            services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(database));
            services.AddScoped<IUserPermissionGrantRepository, EfUserPermissionGrantRepository>();
            services.AddScoped<IRoleDefinitionRepository, EfRoleDefinitionRepository>();
            services.AddScoped<IWorkspaceAdministratorReader, EfWorkspaceAdministratorReader>();
            services.AddScoped<IMembershipService, MembershipService>();
            services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
                .AddCookie(o => o.Events.OnValidatePrincipal = PortalSession.ValidateCookieAsync);
            Services = services.BuildServiceProvider();
            Db = Services.GetRequiredService<AppDbContext>();
            User = new UserRow { Id = Guid.NewGuid(), Email = "synthetic@example.test", DisplayName = "Synthetic",
                Role = "Owner", IsDefaultAdmin = true, PasswordHash = "unused", PasswordSet = true,
                PhoneNumber = "+15555550100", CreatedAt = DateTimeOffset.UtcNow };
            Db.Users.Add(User);
            Db.CommunicationProviders.Add(new CommunicationProviderRow { Id = Guid.NewGuid(), Name = "Test",
                Channel = "sms", Kind = "twilio", Enabled = true, IsDefault = true, UseForTwoFactor = true,
                AuthType = "none", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow });
            Db.SaveChanges();
            var sender = Stub.Create<IClientCommunicationSender>((method, args) => {
                if (method != "SendAuthenticationSmsAsync") throw new NotSupportedException(method);
                Sms = (string)args[1]!;
                return Task.FromResult(new ClientMessageDelivery("test", "test"));
            });
            var providers = new CommunicationProviderService(new EfCommunicationProviderStore(Db), Db,
                new EfProjectSecretRepository(Db), Stub.Create<ISecretProtector>((_, args) => args[0]));
            Auth = new AuthService(Db, sender, providers);
        }
        public ClaimsPrincipal Principal() => new(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, User.Id.ToString()), new Claim("tenant", Tenant.TenantId.ToString()),
            new Claim(ClaimTypes.Role, "Owner")], CookieAuthenticationDefaults.AuthenticationScheme));
        public DefaultHttpContext Http() => new() { RequestServices = Services };
        public string StateJson(Guid? tenant = null, bool needsPhone = false) => JsonSerializer.Serialize(new {
            UserId = User.Id, TenantId = tenant ?? Tenant.TenantId, Nonce = "test-browser", NeedsPhone = needsPhone,
            Channel = "sms", SmsAvailable = true, ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10), ReturnUrl = "/"
        });
        public string State(bool needsPhone = false) => Protection.CreateProtector(AuthController.MfaPurpose).ToTimeLimitedDataProtector()
            .Protect(StateJson(needsPhone: needsPhone), TimeSpan.FromMinutes(10));
        public AuthController Controller()
        {
            // Antiforgery is independent: anonymous browsers can obtain a valid form token.
            var antiforgery = Stub.Create<IAntiforgery>((method, _) => method == "ValidateRequestAsync"
                ? Task.CompletedTask : throw new NotSupportedException(method));
            var authentication = Stub.Create<IAuthenticationService>((method, args) => {
                if (method != "SignInAsync") throw new NotSupportedException(method);
                SignedIn = (ClaimsPrincipal)args[2]!; return Task.CompletedTask;
            });
            var http = new DefaultHttpContext { RequestServices = new ServiceCollection().AddSingleton(authentication).BuildServiceProvider() };
            http.Request.Headers.Cookie = AuthController.MfaCookie + "=test-browser";
            return new AuthController(Auth, new PortalToken(), Services.GetRequiredService<IMembershipService>(),
                antiforgery, new ConfigurationBuilder().Build(), null!, Protection, null!, Tenant)
                { ControllerContext = new ControllerContext { HttpContext = http } };
        }
        public void Dispose() => Services.Dispose();
    }
    public class Stub : DispatchProxy
    {
        public Func<string, object?[], object?> Handler = null!;
        public static T Create<T>(Func<string, object?[], object?> handler) where T : class {
            var proxy = DispatchProxy.Create<T, Stub>(); ((Stub)(object)proxy).Handler = handler; return proxy;
        }
        protected override object? Invoke(MethodInfo? method, object?[]? args) => Handler(method!.Name, args ?? []);
    }
    private sealed class MutableTenant : ICurrentTenant
    {
        public Guid TenantId { get; set; } = Guid.NewGuid();
        public string Slug => "test";
        public string TimeZoneId => "UTC";
        public bool IsResolved => true;
    }
    private sealed class CircuitAuthentication(ClaimsPrincipal user) : AuthenticationStateProvider, IHostEnvironmentAuthenticationStateProvider
    {
        private Task<AuthenticationState> _state = Task.FromResult(new AuthenticationState(user));
        public override Task<AuthenticationState> GetAuthenticationStateAsync() => _state;
        public void SetAuthenticationState(Task<AuthenticationState> state) => _state = state;
    }
}
