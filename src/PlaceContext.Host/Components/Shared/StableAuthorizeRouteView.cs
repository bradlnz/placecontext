using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Rendering;
using RouteData = Microsoft.AspNetCore.Components.RouteData;

namespace PlaceContext.Host.Components.Shared;

public sealed class StableAuthorizeRouteView : AuthorizeViewCore
{
    [Parameter, EditorRequired] public RouteData RouteData { get; set; } = default!;
    [Parameter] public Type? DefaultLayout { get; set; }
    [CascadingParameter] private Task<AuthenticationState>? AuthenticationTask { get; set; }

    private readonly SemaphoreSlim _authorization = new(1, 1);
    private int _pendingChecks;
    private RouteData? _displayedRoute;
    private Task<AuthenticationState>? _displayedAuthentication;

    public StableAuthorizeRouteView()
    {
        Authorized = _ => builder =>
        {
            _displayedRoute = RouteData;
            _displayedAuthentication = AuthenticationTask;
            builder.OpenComponent<RouteView>(0);
            builder.AddAttribute(1, nameof(RouteView.RouteData), RouteData);
            builder.AddAttribute(2, nameof(RouteView.DefaultLayout), DefaultLayout);
            builder.CloseComponent();
        };
    }

    protected override IAuthorizeData[]? GetAuthorizeData()
    {
        var attributes = RouteData.PageType.GetCustomAttributes(inherit: true);
        if (attributes.OfType<IAllowAnonymous>().Any())
        {
            return null;
        }
        var policies = attributes.OfType<IAuthorizeData>().ToArray();
        return policies.Length == 0 ? null : policies;
    }

    protected override async Task OnParametersSetAsync()
    {
        _pendingChecks++;
        try
        {
            // Serialize checks so a slow, older authorization cannot overwrite the latest result.
            await _authorization.WaitAsync();
            try
            {
                await base.OnParametersSetAsync();
            }
            finally
            {
                _authorization.Release();
            }
        }
        finally
        {
            _pendingChecks--;
        }
    }

    protected override bool ShouldRender() => _pendingChecks == 0
        || _displayedRoute?.PageType != RouteData.PageType
        || !Equals(_displayedRoute.RouteValues.GetValueOrDefault("ProjectId"), RouteData.RouteValues.GetValueOrDefault("ProjectId"))
        || !ReferenceEquals(_displayedAuthentication, AuthenticationTask);

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        if (_pendingChecks > 0)
        {
            _displayedRoute = null;
            builder.AddContent(0, Authorizing);
            return;
        }

        // Only the Authorized fragment may retain a page for the next navigation.
        _displayedRoute = null;
        base.BuildRenderTree(builder);
    }
}
