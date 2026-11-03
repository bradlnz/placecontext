using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application;
using PlaceContext.Host.Auth;
using PlaceContext.Host.Components.ViewModels;

namespace PlaceContext.Host.Tests;

public sealed class McpConnectionFormTests
{
    [Fact]
    public async Task Add_uses_the_current_project_and_clears_credentials_after_success()
    {
        var dispatcher = new Dispatcher();
        var vm = Create(dispatcher);
        vm.Name = "Tools";
        vm.Endpoint = "https://example.test/mcp";
        vm.AuthType = "bearer";
        vm.AuthToken = "test-token";
        var project = Guid.NewGuid();
        var result = await vm.SaveAsync(project);
        Assert.NotNull(result);
        Assert.Equal(project, dispatcher.Saved!.ProjectId);
        Assert.Equal(vm.Endpoint, dispatcher.Saved.EndpointUrl);
        Assert.Equal("test-token", dispatcher.Saved.AuthToken);
        Assert.Null(dispatcher.Saved.Command);
        Assert.Empty(vm.AuthToken);
        Assert.False(vm.Saving);
    }

    [Fact]
    public async Task Denied_add_never_calls_the_service()
    {
        var dispatcher = new Dispatcher();
        var vm = Create(dispatcher, allowed: false);
        Assert.Null(await vm.SaveAsync(Guid.NewGuid()));
        Assert.Null(dispatcher.Saved);
        Assert.Contains("administrators", vm.Error);
    }

    [Fact]
    public async Task Failed_add_preserves_input_for_retry_and_stdio_omits_endpoint()
    {
        var dispatcher = new Dispatcher { Fail = true };
        var vm = Create(dispatcher);
        vm.Name = "Local tools";
        vm.Transport = "stdio";
        vm.Command = "npx";
        vm.Args = "[\"server\"]";
        vm.Endpoint = "https://unused.test";
        Assert.Null(await vm.SaveAsync(Guid.NewGuid()));
        Assert.Equal("Local tools", vm.Name);
        Assert.NotNull(vm.Error);
        Assert.False(vm.Saving);
        dispatcher.Fail = false;
        Assert.NotNull(await vm.SaveAsync(Guid.NewGuid()));
        Assert.Null(dispatcher.Saved!.EndpointUrl);
        Assert.Equal("npx", dispatcher.Saved.Command);
        Assert.Null(vm.Error);
    }

    private static McpConnectionFormViewModel Create(Dispatcher dispatcher, bool allowed = true) =>
        new(new PlaceContextService(dispatcher, null!, null!, null!), new Authentication(), new Authorization(allowed));

    private sealed class Authentication : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
            Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity("test"))));
    }
    private sealed class Authorization(bool allowed) : IAuthorizationService
    {
        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, string policyName)
        {
            Assert.Equal(Policies.DefaultAdmin, policyName);
            return Task.FromResult(allowed ? AuthorizationResult.Success() : AuthorizationResult.Failed());
        }
        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, IEnumerable<IAuthorizationRequirement> requirements) => throw new NotSupportedException();
    }
    private sealed class Dispatcher : IDispatcher
    {
        public CreateMcpConnectionCommand? Saved;
        public bool Fail;
        public Task<TResult> Send<TResult>(ICommand<TResult> command, CancellationToken ct = default)
        {
            Saved = Assert.IsType<CreateMcpConnectionCommand>(command);
            if (Fail)
            {
                throw new InvalidOperationException("Could not save.");
            }
            object result = new McpConnectionView(Guid.NewGuid(), Saved.ProjectId, Saved.Name, Saved.Transport,
                            Saved.EndpointUrl, Saved.Command, Saved.Args, Saved.AuthType, true, null, null, DateTimeOffset.UtcNow);
            return Task.FromResult((TResult)result);
        }
        public Task<TResult> Query<TResult>(IQuery<TResult> query, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
