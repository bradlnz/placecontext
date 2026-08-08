using Microsoft.JSInterop;
using PlaceContext.Application.Ports;
using PlaceContext.Host;

namespace PlaceContext.Host.Components.ViewModels;

public sealed class ApiTokensSettingsViewModel(IUserApiTokenService tokens, PortalUiState ui, IJSRuntime js)
    : PageViewModel
{
    public const string DefaultLifetimeDays = "90";
    public IReadOnlyList<UserApiTokenView> Tokens { get; private set; } =
        Array.Empty<UserApiTokenView>();
    public IReadOnlyList<ApiEndpoint> UsableEndpoints { get; } =
        new[]
        {
            new ApiEndpoint(
                "GET",
                "/api/v1/entities",
                "List entities for the resolved project."
            ),
            new ApiEndpoint(
                "GET",
                "/api/v1/{entity-name}",
                "List rows for an entity/table (query: search, page, pageSize)."
            ),
            new ApiEndpoint(
                "GET",
                "/api/v1/{entity-name}/{key}",
                "Look up one entity row by label/first-column key."
            ),
            new ApiEndpoint(
                "POST",
                "/api/v1/{job-name}",
                "Run a named job with API invocation enabled."
            ),
            new ApiEndpoint(
                "POST",
                "/api/v1/{entity-name}/jobs/{jobId}/run",
                "Run a specific job on an entity with API invocation enabled."
            ),
            new ApiEndpoint(
                "GET",
                "/api/v1/search?q=<query>",
                "Search within the resolved project."
            )
        };
    public IReadOnlyList<ApiEndpointExample> EndpointExamples { get; } =
        new[]
        {
            new ApiEndpointExample(
                "List entities",
                "curl -H \"Authorization: Bearer $PC_TOKEN\" -H \"X-Project-Id: $PC_PROJECT_ID\" \\\n  \"$PC_HOST/api/v1/entities\""
            ),
            new ApiEndpointExample(
                "Query an entity",
                "curl -H \"Authorization: Bearer $PC_TOKEN\" -H \"X-Project: $PC_PROJECT_NAME\" \\\n  \"$PC_HOST/api/v1/example-entity?search=acme&page=1&pageSize=20\""
            ),
            new ApiEndpointExample(
                "Run job by name",
                "curl -X POST -H \"Authorization: Bearer $PC_TOKEN\" -H \"X-Project-Id: $PC_PROJECT_ID\" \\\n  -H \"Content-Type: application/json\" \\\n  -d '{\"inputPayload\":\"{\\\"name\\\":\\\"Acme\\\"}\"}' \\\n  \"$PC_HOST/api/v1/build-summary\""
            )
        };
    public bool Loading { get; private set; } = true;
    public bool Busy { get; private set; }
    public string? Message { get; private set; }
    public string? CreateError { get; private set; }
    public string NewName { get; set; } = "";
    public string LifetimeDays { get; set; } = DefaultLifetimeDays;
    public string? CreatedRaw { get; private set; }
    public string? CreatedPrefix { get; private set; }
    public async Task CopyExampleAsync(string command)
    {
        await js.InvokeVoidAsync("navigator.clipboard.writeText", command);
    }

    public async Task LoadAsync()
    {
        ui.Set("API tokens", "personal tokens for project data and search");
        Loading = true;
        NotifyStateChanged();
        try
        {
            Tokens = await tokens.ListMineAsync();
        }
        catch (Exception ex)
        {
            Message = ex.Message;
        }
        finally
        {
            Loading = false;
            NotifyStateChanged();
        }
    }

    public void DismissCreated()
    {
        CreatedRaw = null;
        NotifyStateChanged();
    }

    public async Task CreateAsync()
    {
        CreateError = null;
        CreatedRaw = null;
        if (string.IsNullOrWhiteSpace(NewName))
        {
            CreateError = "Give the token a name.";
            return;
        }
        Busy = true;
        NotifyStateChanged();
        try
        {
            var days =
                int.TryParse(LifetimeDays, out var parsed) && parsed > 0
                    ? parsed
                    : int.Parse(DefaultLifetimeDays);
            var created = await tokens.CreateAsync(NewName.Trim(), TimeSpan.FromDays(days));
            CreatedRaw = created.RawToken;
            CreatedPrefix = created.TokenPrefix;
            NewName = "";
            await LoadAsync();
        }
        catch (Exception ex)
        {
            CreateError = ex.Message;
        }
        finally
        {
            Busy = false;
            NotifyStateChanged();
        }
    }

    public async Task RevokeAsync(Guid id)
    {
        Busy = true;
        NotifyStateChanged();
        try
        {
            await tokens.RevokeAsync(id);
            Message = "Token revoked.";
            await LoadAsync();
        }
        catch (Exception ex)
        {
            Message = ex.Message;
        }
        finally
        {
            Busy = false;
            NotifyStateChanged();
        }
    }
}
