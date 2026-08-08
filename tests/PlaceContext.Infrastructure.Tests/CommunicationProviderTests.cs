using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PlaceContext.Application.Ports;
using PlaceContext.Infrastructure.Comms;
using PlaceContext.Infrastructure.Persistence;
using PlaceContext.TestSupport;
using PlaceContext.Vault.Domain.Repositories;

namespace PlaceContext.Infrastructure.Tests;

public sealed class CommunicationProviderTests
{
    [Fact]
    public async Task Create_validates_name_channel_and_kind()
    {
        await using var fixture = await Fixture.CreateAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.CreateAsync(
            fixture.Input("email", "postmark", name: "")));
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.CreateAsync(
            fixture.Input("email", "twilio")));
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.CreateAsync(
            fixture.Input("sms", "postmark")));
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.CreateAsync(
            fixture.Input("pigeon", "postmark")));
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.CreateAsync(
            fixture.Input("email", "mailchimp")));
    }

    [Fact]
    public async Task Create_validates_auth_and_settings_shapes()
    {
        await using var fixture = await Fixture.CreateAsync();

        // header auth needs a header name
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.CreateAsync(
            fixture.Input("email", "postmark", authType: "header", authHeaderName: "")));
        // basic auth needs the username (Twilio Account SID) in settings
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.CreateAsync(
            fixture.Input("sms", "twilio", authType: "basic", settings: """{"fromNumber":"+15551234567"}""")));
        // settings must be valid JSON
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.CreateAsync(
            fixture.Input("email", "postmark", settings: "not-json")));
        // email kinds need a verified sender address
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.CreateAsync(
            fixture.Input("email", "postmark", settings: """{"fromName":"Team"}""")));
        // twilio needs a sender number
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.CreateAsync(
            fixture.Input("sms", "twilio", authType: "basic",
                settings: """{"accountSid":"AC123"}""")));
    }

    [Fact]
    public async Task Create_rejects_a_missing_vault_secret_reference()
    {
        await using var fixture = await Fixture.CreateAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.CreateAsync(
            fixture.Input("email", "postmark", secretName: "NOPE")));
        Assert.Contains("NOPE", ex.Message);
    }

    [Fact]
    public async Task SetDefault_is_exclusive_per_channel()
    {
        await using var fixture = await Fixture.CreateAsync();
        var first = await fixture.Service.CreateAsync(fixture.Input("email", "postmark", name: "Primary"));
        var second = await fixture.Service.CreateAsync(fixture.Input("email", "sendgrid", name: "Backup",
            authType: "bearer", settings: """{"fromEmail":"hello@example.test","fromName":"Team"}"""));
        var sms = await fixture.Service.CreateAsync(fixture.Input("sms", "twilio", name: "Texts",
            authType: "basic", settings: """{"accountSid":"AC123","fromNumber":"+15551234567"}"""));

        await fixture.Service.SetDefaultAsync(first.Id);
        await fixture.Service.SetDefaultAsync(sms.Id);
        await fixture.Service.SetDefaultAsync(second.Id);

        var providers = await fixture.Service.ListAsync();
        Assert.False(providers.Single(p => p.Id == first.Id).IsDefault);
        Assert.True(providers.Single(p => p.Id == second.Id).IsDefault);
        // the sms channel default is untouched by email-channel changes
        Assert.True(providers.Single(p => p.Id == sms.Id).IsDefault);
    }

    [Fact]
    public async Task SetTwoFactor_is_exclusive_per_channel()
    {
        await using var fixture = await Fixture.CreateAsync();
        var first = await fixture.Service.CreateAsync(fixture.Input("email", "postmark", name: "Primary"));
        var second = await fixture.Service.CreateAsync(fixture.Input("email", "sendgrid", name: "Backup",
            authType: "bearer", settings: """{"fromEmail":"hello@example.test","fromName":"Team"}"""));

        await fixture.Service.SetTwoFactorAsync(first.Id, true);
        await fixture.Service.SetTwoFactorAsync(second.Id, true);

        var providers = await fixture.Service.ListAsync();
        Assert.False(providers.Single(p => p.Id == first.Id).UseForTwoFactor);
        Assert.True(providers.Single(p => p.Id == second.Id).UseForTwoFactor);

        await fixture.Service.SetTwoFactorAsync(second.Id, false);
        Assert.False((await fixture.Service.GetAsync(second.Id))!.UseForTwoFactor);
    }

    [Fact]
    public async Task ResolveForTwoFactor_prefers_the_flagged_provider_then_falls_back_to_default()
    {
        await using var fixture = await Fixture.CreateAsync();
        var fallback = await fixture.Service.CreateAsync(fixture.Input("email", "postmark", name: "Default"));
        var flagged = await fixture.Service.CreateAsync(fixture.Input("email", "sendgrid", name: "Auth",
            authType: "bearer", settings: """{"fromEmail":"hello@example.test","fromName":"Team"}"""));
        await fixture.Service.SetDefaultAsync(fallback.Id);

        // no flag set — resolves the channel default
        var resolved = await fixture.Service.ResolveForTwoFactorAsync("email");
        Assert.Equal(fallback.Id, resolved!.Id);

        await fixture.Service.SetTwoFactorAsync(flagged.Id, true);
        resolved = await fixture.Service.ResolveForTwoFactorAsync("email");
        Assert.Equal(flagged.Id, resolved!.Id);
        Assert.Equal("postmark-key", resolved.Secret);
        Assert.True(resolved.SecretResolved);
    }

    [Fact]
    public async Task Resolve_reports_the_secret_unresolved_when_it_disappears_from_vault()
    {
        await using var fixture = await Fixture.CreateAsync();
        var provider = await fixture.Service.CreateAsync(fixture.Input("email", "postmark"));
        await fixture.Service.SetDefaultAsync(provider.Id);
        await fixture.Secrets.DeleteAsync(fixture.VaultProjectId, "API_KEY");
        await fixture.Db.SaveChangesAsync();

        var resolved = await fixture.Service.ResolveForSendAsync("email");

        Assert.NotNull(resolved);
        Assert.False(resolved!.SecretResolved);
        Assert.Null(resolved.Secret);
    }

    [Fact]
    public async Task Postmark_send_uses_header_auth_and_the_transactional_payload()
    {
        await using var fixture = await Fixture.CreateAsync();
        var provider = await fixture.Service.CreateAsync(fixture.Input("email", "postmark",
            settings: """{"fromEmail":"hello@example.test","fromName":"Example Team","messageStream":"outbound"}"""));
        await fixture.Service.SetDefaultAsync(provider.Id);
        await fixture.Service.SetTwoFactorAsync(provider.Id, true);

        var sender = fixture.Sender();
        var delivery = await sender.SendEmailAsync(
            "ada@example.test", "Ada Lovelace", "Welcome", "Hello Ada",
            attachments: new[]
            {
                new ClientEmailAttachment("report.pdf", "application/pdf", "UERG")
            });

        Assert.Equal("Postmark", delivery.Provider);
        Assert.Equal("message-123", delivery.ExternalId);
        var captured = fixture.Captured!;
        Assert.Equal("postmark-key", captured.Headers["X-Postmark-Server-Token"]);
        Assert.Contains("\"MessageStream\":\"outbound\"", captured.Body);
        Assert.Contains("\"Tag\":\"crm-transactional\"", captured.Body);
        Assert.Contains("\"From\":\"\\u0022Example Team\\u0022 \\u003Chello@example.test\\u003E\"", captured.Body);
        Assert.Contains("\"Attachments\":[{\"Name\":\"report.pdf\",\"Content\":\"UERG\",\"ContentType\":\"application/pdf\"}]", captured.Body);

        await sender.SendAuthenticationEmailAsync(
            "ada@example.test", "Ada Lovelace", "Verification code", "Code: 123456");
        Assert.Contains("\"Tag\":\"authentication\"", fixture.Captured!.Body);
    }

    [Fact]
    public async Task SendGrid_send_uses_bearer_auth()
    {
        await using var fixture = await Fixture.CreateAsync();
        var provider = await fixture.Service.CreateAsync(fixture.Input("email", "sendgrid",
            authType: "bearer",
            settings: """{"fromEmail":"hello@example.test","fromName":"Example Team","endpoint":"https://sendgrid.test/mail"}"""));
        await fixture.Service.SetDefaultAsync(provider.Id);
        fixture.RespondWith("""{"ok":true}""",
            headers: new Dictionary<string, string> { ["X-Message-Id"] = "sg-1" });

        var delivery = await fixture.Sender().SendEmailAsync(
            "ada@example.test", "Ada Lovelace", "Welcome", "Hello Ada");

        Assert.Equal("SendGrid", delivery.Provider);
        Assert.Equal("sg-1", delivery.ExternalId);
        Assert.Equal("Bearer postmark-key", fixture.Captured!.Headers["Authorization"]);
        Assert.Contains("\"subject\":\"Welcome\"", fixture.Captured.Body);
    }

    [Fact]
    public async Task Twilio_send_uses_basic_auth_with_the_account_sid()
    {
        await using var fixture = await Fixture.CreateAsync();
        var provider = await fixture.Service.CreateAsync(fixture.Input("sms", "twilio",
            authType: "basic",
            settings: """{"accountSid":"AC123","fromNumber":"+15551234567","endpoint":"https://twilio.test"}"""));
        await fixture.Service.SetDefaultAsync(provider.Id);
        fixture.RespondWith("""{"sid":"SM123"}""");

        var delivery = await fixture.Sender().SendSmsAsync("+15557654321", "Your code is 123456");

        Assert.Equal("Twilio", delivery.Provider);
        Assert.Equal("SM123", delivery.ExternalId);
        var expected = Convert.ToBase64String(Encoding.UTF8.GetBytes("AC123:postmark-key"));
        Assert.Equal($"Basic {expected}", fixture.Captured!.Headers["Authorization"]);
        Assert.Contains("To=%2B15557654321", fixture.Captured.Body);
        Assert.Contains("From=%2B15551234567", fixture.Captured.Body);
    }

    [Fact]
    public async Task Capability_is_disabled_when_the_referenced_vault_secret_is_missing()
    {
        await using var fixture = await Fixture.CreateAsync();
        var provider = await fixture.Service.CreateAsync(fixture.Input("email", "postmark"));
        await fixture.Service.SetDefaultAsync(provider.Id);
        await fixture.Secrets.DeleteAsync(fixture.VaultProjectId, "API_KEY");
        await fixture.Db.SaveChangesAsync();

        var capabilities = await fixture.Sender().GetCapabilitiesAsync();

        Assert.False(capabilities.EmailEnabled);
        Assert.False(capabilities.SmsEnabled);
    }

    [Fact]
    public async Task Sending_without_a_configured_provider_reports_not_configured()
    {
        await using var fixture = await Fixture.CreateAsync(withSecret: false);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Sender().SendEmailAsync("ada@example.test", "Ada", "Hi", "Body"));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Sender().SendSmsAsync("+15557654321", "Body"));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Sender().SendAuthenticationSmsAsync("+15557654321", "Body"));
    }

    [Fact]
    public async Task SendTest_uses_the_specified_provider_not_the_default()
    {
        await using var fixture = await Fixture.CreateAsync();
        var fallback = await fixture.Service.CreateAsync(fixture.Input("email", "postmark", name: "Default"));
        var backup = await fixture.Service.CreateAsync(fixture.Input("email", "sendgrid", name: "Backup",
            authType: "bearer",
            settings: """{"fromEmail":"hello@example.test","fromName":"Example Team","endpoint":"https://sendgrid.test/mail"}"""));
        await fixture.Service.SetDefaultAsync(fallback.Id);
        fixture.RespondWith("""{"ok":true}""",
            headers: new Dictionary<string, string> { ["X-Message-Id"] = "sg-test" });

        var delivery = await fixture.Sender().SendTestAsync(backup.Id, "ada@example.test");

        // the non-default SendGrid provider handled the send, not the Postmark default
        Assert.Equal("SendGrid", delivery.Provider);
        Assert.Equal("sg-test", delivery.ExternalId);
        Assert.Equal("Bearer postmark-key", fixture.Captured!.Headers["Authorization"]);
        Assert.Contains("\"subject\":\"PlaceContext test message\"", fixture.Captured.Body);
        Assert.Contains("\"email\":\"ada@example.test\"", fixture.Captured.Body);
    }

    [Fact]
    public async Task SendTest_sends_sms_through_the_specified_twilio_provider()
    {
        await using var fixture = await Fixture.CreateAsync();
        var provider = await fixture.Service.CreateAsync(fixture.Input("sms", "twilio",
            authType: "basic",
            settings: """{"accountSid":"AC123","fromNumber":"+15551234567","endpoint":"https://twilio.test"}"""));
        fixture.RespondWith("""{"sid":"SMtest"}""");

        var delivery = await fixture.Sender().SendTestAsync(provider.Id, "+15557654321");

        Assert.Equal("Twilio", delivery.Provider);
        Assert.Equal("SMtest", delivery.ExternalId);
        var expected = Convert.ToBase64String(Encoding.UTF8.GetBytes("AC123:postmark-key"));
        Assert.Equal($"Basic {expected}", fixture.Captured!.Headers["Authorization"]);
        Assert.Contains("To=%2B15557654321", fixture.Captured.Body);
        Assert.Contains("test+message", fixture.Captured.Body);
    }

    [Fact]
    public async Task SendTest_throws_for_an_unknown_provider()
    {
        await using var fixture = await Fixture.CreateAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Sender().SendTestAsync(Guid.NewGuid(), "ada@example.test"));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            fixture.Sender().SendTestAsync(Guid.NewGuid(), " "));
    }

    private sealed record CapturedRequest(
        IReadOnlyDictionary<string, string> Headers,
        string Body);

    /// <summary>Shared mutable state between the stub HTTP handler and the fixture.</summary>
    private sealed class StubState
    {
        public CapturedRequest? Captured;
        public Func<HttpResponseMessage> Respond = () => Json("""
            {"ErrorCode":0,"Message":"OK","MessageID":"message-123","SubmittedAt":"2026-07-31T00:00:00Z","To":"ada@example.test"}
            """);

        public static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly IHttpClientFactory _factory;
        private readonly StubState _state;

        public AppDbContext Db { get; }
        public IProjectSecretRepository Secrets { get; }
        public CommunicationProviderService Service { get; }
        public Guid VaultProjectId { get; }
        public CapturedRequest? Captured => _state.Captured;

        private Fixture(
            AppDbContext db,
            IProjectSecretRepository secrets,
            CommunicationProviderService service,
            IHttpClientFactory factory,
            StubState state,
            Guid vaultProjectId)
            => (Db, Secrets, Service, _factory, _state, VaultProjectId)
                = (db, secrets, service, factory, state, vaultProjectId);

        public static async Task<Fixture> CreateAsync(
            string secretValue = "postmark-key", bool withSecret = true)
        {
            var tenant = new FakeCurrentTenant(Guid.NewGuid());
            var dbOptions = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .Options;
            var db = new AppDbContext(dbOptions, tenant);
            var secrets = new InMemoryProjectSecretRepository();
            var vaultProjectId = Guid.NewGuid();
            if (withSecret)
            {
                await secrets.AddAsync(vaultProjectId, "API_KEY", secretValue, DateTimeOffset.UtcNow);
            }

            var state = new StubState();
            var handler = new StubHandler(async request =>
            {
                state.Captured = new CapturedRequest(
                    request.Headers.ToDictionary(x => x.Key, x => string.Join(",", x.Value)),
                    request.Content is null ? "" : await request.Content.ReadAsStringAsync());
                return state.Respond();
            });
            var factory = new StubHttpClientFactory(handler);
            var service = new CommunicationProviderService(db, secrets, new PlaintextSecretProtector());
            return new Fixture(db, secrets, service, factory, state, vaultProjectId);
        }

        public CommunicationProviderInput Input(
            string channel,
            string kind,
            string name = "Provider",
            string authType = "header",
            string? authHeaderName = "X-Postmark-Server-Token",
            string secretName = "API_KEY",
            string? settings = null)
            => new(
                channel,
                kind,
                name,
                Enabled: true,
                authType,
                authHeaderName,
                VaultProjectId,
                secretName,
                settings ?? """{"fromEmail":"hello@example.test","fromName":"Example Team"}""");

        public void RespondWith(string body, Dictionary<string, string>? headers = null)
            => _state.Respond = () =>
            {
                var response = StubState.Json(body);
                foreach (var (key, value) in headers ?? new())
                    response.Headers.TryAddWithoutValidation(key, value);
                return response;
            };

        public DatabaseCommunicationSender Sender()
            => new(
                _factory,
                Options.Create(new ClientCommsOptions
                {
                    Postmark = new PostmarkOptions { ApiEndpoint = "https://api.postmark.test" },
                }),
                Service);

        public async ValueTask DisposeAsync() => await Db.DisposeAsync();
    }

    private sealed class PlaintextSecretProtector : ISecretProtector
    {
        public string Protect(string plaintext) => plaintext;
        public string Unprotect(string ciphertext) => ciphertext;
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _send;
        public StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) => _send = send;
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) => _send(request);
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public StubHttpClientFactory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }
}
