using System.Security.Claims;
using System.Threading.Channels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PlaceContext.Host.Components.Shared;

namespace PlaceContext.Host.Tests;

public sealed class StableAuthorizeRouteViewTests
{
    [Fact]
    public async Task Same_page_navigation_keeps_the_authorized_instance_until_the_new_check_finishes()
    {
        await using var fixture = new Fixture();
        await fixture.Open();
        var page = Assert.Single(fixture.Pages);
        var loading = fixture.LoadingRenders;

        var render = await fixture.Navigate("runs");
        var decision = await fixture.NextDecision();
        Assert.Equal("canvas", page.View);
        Assert.False(page.Disposed);
        Assert.Equal(loading, fixture.LoadingRenders);

        fixture.Resolve(decision, allowed: true);
        await render.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Same(page, Assert.Single(fixture.Pages));
        Assert.Equal("runs", page.View);
        Assert.Equal(loading, fixture.LoadingRenders);
    }

    [Fact]
    public async Task Denied_navigation_removes_the_previous_page_without_rendering_the_target()
    {
        await using var fixture = new Fixture();
        await fixture.Open();
        var page = Assert.Single(fixture.Pages);
        var render = await fixture.Navigate("runs");
        fixture.Resolve(await fixture.NextDecision(), allowed: false);
        await render.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(page.Disposed);
        Assert.Equal("canvas", page.View);
        Assert.Single(fixture.Pages);
        Assert.True(fixture.Denied);
    }

    [Theory]
    [InlineData("project")]
    [InlineData("page")]
    [InlineData("identity")]
    public async Task Different_security_context_does_not_retain_the_old_page(string change)
    {
        await using var fixture = new Fixture();
        await fixture.Open();
        var page = Assert.Single(fixture.Pages);
        var render = await fixture.Navigate("runs", change);
        var decision = await fixture.NextDecision();
        Assert.True(page.Disposed);
        Assert.Single(fixture.Pages);
        fixture.Resolve(decision, allowed: false);
        await render.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(fixture.Denied);
    }

    [Fact]
    public async Task A_slow_allowed_check_cannot_render_a_newer_denied_route()
    {
        await using var fixture = new Fixture();
        await fixture.Open();
        var first = await fixture.Navigate("list");
        var olderDecision = await fixture.NextDecision();
        var second = await fixture.Navigate("runs");
        olderDecision.SetResult(AuthorizationResult.Success());
        var latestDecision = await fixture.NextDecision();
        var page = Assert.Single(fixture.Pages);
        Assert.Equal("canvas", page.View);
        fixture.Resolve(latestDecision, allowed: false);
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("canvas", page.View);
        Assert.True(page.Disposed);
        Assert.True(fixture.Denied);
    }

    [Authorize]
    public class ProbePage : ComponentBase, IDisposable
    {
        [Inject] public List<ProbePage> Pages { get; set; } = default!;
        [Parameter] public Guid ProjectId { get; set; }
        [Parameter] public string? View { get; set; }
        public bool Disposed { get; private set; }
        protected override void OnInitialized() => Pages.Add(this);
        public void Dispose() => Disposed = true;
    }

    public sealed class OtherPage : ProbePage;

    private sealed class Authorization : IAuthorizationService
    {
        public AuthorizationResult? FollowupResult { get; set; }
        public Channel<TaskCompletionSource<AuthorizationResult>> Decisions { get; } = Channel.CreateUnbounded<TaskCompletionSource<AuthorizationResult>>();
        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, IEnumerable<IAuthorizationRequirement> requirements)
        {
            if (FollowupResult is { } result)
            {
                return Task.FromResult(result);
            }
            var decision = new TaskCompletionSource<AuthorizationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            Decisions.Writer.TryWrite(decision);
            return decision.Task;
        }
        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, string policyName) => throw new NotSupportedException();
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public List<ProbePage> Pages { get; } = [];
        public int LoadingRenders { get; private set; }
        public bool Denied { get; private set; }
        private readonly Guid _projectId = Guid.NewGuid();
        private Task<AuthenticationState> _authentication = Authentication();
        private readonly Authorization _authorization = new();
        private readonly ServiceProvider _services;
        private readonly TestRenderer _renderer;
        private int? _root;

        public Fixture()
        {
            _services = new ServiceCollection().AddLogging().AddAuthorizationCore()
                .AddSingleton(Pages).AddSingleton<IAuthorizationService>(_authorization).BuildServiceProvider();
            _renderer = new TestRenderer(_services, _services.GetRequiredService<ILoggerFactory>());
        }

        public async Task Open()
        {
            var render = await Navigate("canvas");
            var decision = await NextDecision();
            Assert.Empty(Pages);
            Assert.True(LoadingRenders > 0);
            Resolve(decision, allowed: true);
            await render.WaitAsync(TimeSpan.FromSeconds(5));
        }

        public void Resolve(TaskCompletionSource<AuthorizationResult> decision, bool allowed)
        {
            // A changed cascade can schedule another parameter update for the same route.
            _authorization.FollowupResult = allowed ? AuthorizationResult.Success() : AuthorizationResult.Failed();
            decision.SetResult(_authorization.FollowupResult);
        }

        public Task<TaskCompletionSource<AuthorizationResult>> NextDecision() =>
            _authorization.Decisions.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        public async Task<Task> Navigate(string view, string? change = null)
        {
            Task render = Task.CompletedTask;
            await _renderer.Dispatcher.InvokeAsync(() =>
            {
                _authorization.FollowupResult = null;
                _root ??= _renderer.Add(new CascadingValue<Task<AuthenticationState>>());
                if (change == "identity")
                {
                    _authentication = Authentication();
                }
                var route = new RouteData(change == "page" ? typeof(OtherPage) : typeof(ProbePage),
                    new Dictionary<string, object?> { ["ProjectId"] = change == "project" ? Guid.NewGuid() : _projectId, ["View"] = view });
                RenderFragment child = builder =>
                {
                    builder.OpenComponent<StableAuthorizeRouteView>(0);
                    builder.AddAttribute(1, "RouteData", route);
                    builder.AddAttribute(2, "Authorizing", (RenderFragment)(_ => LoadingRenders++));
                    builder.AddAttribute(3, "NotAuthorized", (RenderFragment<AuthenticationState>)(_ => _ => Denied = true));
                    builder.CloseComponent();
                };
                render = _renderer.Render(_root.Value, ParameterView.FromDictionary(new Dictionary<string, object?>
                    { ["Value"] = _authentication, ["ChildContent"] = child }));
            });
            return render;
        }

        private static Task<AuthenticationState> Authentication() => Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity("test"))));
        public async ValueTask DisposeAsync()
        {
#pragma warning disable BL0006
            await _renderer.DisposeAsync();
#pragma warning restore BL0006
            await _services.DisposeAsync();
        }
    }

#pragma warning disable BL0006 // Exercise real component lifecycle and overlapping authorization checks.
    private sealed class TestRenderer(IServiceProvider services, ILoggerFactory logger) : Renderer(services, logger)
    {
        public override Dispatcher Dispatcher { get; } = Dispatcher.CreateDefault();
        public int Add(IComponent component) => AssignRootComponentId(component);
        public Task Render(int id, ParameterView parameters) => RenderRootComponentAsync(id, parameters);
        protected override Task UpdateDisplayAsync(in RenderBatch renderBatch) => Task.CompletedTask;
        protected override void HandleException(Exception exception) => throw exception;
    }
#pragma warning restore BL0006
}
