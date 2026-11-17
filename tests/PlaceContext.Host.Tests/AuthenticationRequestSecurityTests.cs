using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PlaceContext.Application;
using PlaceContext.Application.Auth;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Host.Auth;
using PlaceContext.Host.Controllers;
using PlaceContext.TestSupport;

namespace PlaceContext.Host.Tests;

public sealed class AuthenticationRequestSecurityTests
{
    [Fact]
    public async Task Backup_import_rejects_missing_or_invalid_csrf_tokens_and_accepts_a_valid_token()
    {
        await using var host = await Host.StartAsync();
        var token = await host.Client.GetStringAsync("/csrf");
        var manifest = new BackupManifest(3, DateTimeOffset.UtcNow, new("UTC", null, null), [], [], [], [], [], []);
        foreach (var presented in new string?[] { null, "invalid", token })
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/backup/import")
            {
                Content = new StringContent(JsonSerializer.Serialize(manifest), Encoding.UTF8, "text/plain"),
            };
            request.Headers.Add("Origin", "https://attacker.example.test");
            if (presented is not null)
            {
                request.Headers.Add("RequestVerificationToken", presented);
            }
            using var response = await host.Client.SendAsync(request);
            Assert.Equal(presented == token, response.IsSuccessStatusCode);
            if (presented != token)
            {
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
                Assert.Equal(0, host.Imports);
            }
        }
        Assert.Equal(1, host.Imports);
    }

    [Fact]
    public async Task Parallel_logins_are_limited_before_password_verification_even_when_emails_and_forwarded_ips_change()
    {
        await using var host = await Host.StartAsync();
        var token = await host.Client.GetStringAsync("/csrf");
        var statuses = await Task.WhenAll(Enumerable.Range(0, 20).Select(async index =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/auth/login")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["email"] = $"missing-{index}@example.test", ["password"] = "incorrect",
                    ["__RequestVerificationToken"] = token,
                }),
            };
            request.Headers.Add("X-Forwarded-For", $"192.0.2.{index + 1}");
            using var response = await host.Client.SendAsync(request);
            return response.StatusCode;
        }));
        Assert.Equal(10, statuses.Count(status => status == HttpStatusCode.TooManyRequests));
        Assert.Equal(10, statuses.Count(status => status == HttpStatusCode.Redirect));
        Assert.Equal(10, host.PasswordChecks);
    }

    private sealed class Host(WebApplication app) : IAsyncDisposable
    {
        public HttpClient Client { get; private set; } = null!;
        public int Imports;
        public int PasswordChecks;

        public static async Task<Host> StartAsync()
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();
            builder.Services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
            builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
                .AddCookie(options => options.Cookie.SameSite = SameSiteMode.Lax);
            builder.Services.AddAuthorization(options => options.AddPolicy(Policies.DefaultAdmin, policy => policy.RequireAuthenticatedUser()));
            builder.Services.AddAntiforgery();
            builder.Services.AddRateLimiter(options =>
            {
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                options.AddPolicy<string, LoginRateLimitPolicy>("login");
            });
            builder.Services.AddControllers().AddApplicationPart(typeof(BackupController).Assembly).AddControllersAsServices();
            Host host = null!;
            var dispatcher = SecurityBoundaryTests.Stub.Create<IDispatcher>((method, args) =>
            {
                Assert.Equal("Send", method);
                Assert.IsType<ImportManifestCommand>(args[0]);
                Interlocked.Increment(ref host.Imports);
                return Task.FromResult<ImportResultView>(null!);
            });
            var service = new PlaceContextService(dispatcher, null!, null!, null!);
            builder.Services.AddSingleton(service);
            var auth = SecurityBoundaryTests.Stub.Create<IAuthService>((method, _) =>
            {
                Assert.Equal(nameof(IAuthService.ValidateCredentialsAsync), method);
                Interlocked.Increment(ref host.PasswordChecks);
                return Task.FromResult<AuthUser?>(null);
            });
            builder.Services.AddTransient(provider => new AuthController(auth, new PortalToken(), null!,
                provider.GetRequiredService<IAntiforgery>(), builder.Configuration, builder.Environment,
                provider.GetRequiredService<IDataProtectionProvider>(), service, new FakeCurrentTenant(Guid.NewGuid())));
            var app = builder.Build();
            host = new Host(app);
            app.UseRouting();
            app.UseAuthentication();
            app.UseAuthorization();
            app.UseRateLimiter();
            app.UseAntiforgery();
            app.MapGet("/sign-in", async (HttpContext context) => await context.SignInAsync(new ClaimsPrincipal(
                new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "test-admin")], CookieAuthenticationDefaults.AuthenticationScheme))));
            app.MapGet("/csrf", (HttpContext context, IAntiforgery antiforgery) =>
                antiforgery.GetAndStoreTokens(context).RequestToken!);
            app.MapControllers();
            await app.StartAsync();
            host.Client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
            {
                BaseAddress = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()),
            };
            using var login = await host.Client.GetAsync("/sign-in");
            login.EnsureSuccessStatusCode();
            return host;
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }
}
