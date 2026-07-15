package provider

import (
	"context"
	"os"
	"strings"

	"github.com/bradlnz/terraform-provider-placecontext/internal/client"
	"github.com/hashicorp/terraform-plugin-framework/datasource"
	"github.com/hashicorp/terraform-plugin-framework/provider"
	"github.com/hashicorp/terraform-plugin-framework/provider/schema"
	"github.com/hashicorp/terraform-plugin-framework/resource"
	"github.com/hashicorp/terraform-plugin-framework/types"
)

// Ensure the provider satisfies the framework interface.
var _ provider.Provider = (*placecontextProvider)(nil)

// placecontextProvider is the provider implementation.
type placecontextProvider struct {
	version string
}

// New returns a constructor for the provider, wired with a build version.
func New(version string) func() provider.Provider {
	return func() provider.Provider {
		return &placecontextProvider{version: version}
	}
}

// providerModel maps provider schema attributes to Go values.
type providerModel struct {
	Endpoint types.String `tfsdk:"endpoint"`
	APIKey   types.String `tfsdk:"api_key"`
}

func (p *placecontextProvider) Metadata(_ context.Context, _ provider.MetadataRequest, resp *provider.MetadataResponse) {
	resp.TypeName = "placecontext"
	resp.Version = p.version
}

func (p *placecontextProvider) Schema(_ context.Context, _ provider.SchemaRequest, resp *provider.SchemaResponse) {
	resp.Schema = schema.Schema{
		Description: "Manage PlaceContext projects, jobs, and schedules via the workspace management API (/api/v1).",
		Attributes: map[string]schema.Attribute{
			"endpoint": schema.StringAttribute{
				Optional: true,
				Description: "Workspace base URL, e.g. https://acme.placecontext.ai or " +
					"http://acme.localhost:7700 (the /api/v1 suffix is added automatically). " +
					"Falls back to the PLACECONTEXT_ENDPOINT environment variable.",
			},
			"api_key": schema.StringAttribute{
				Optional:  true,
				Sensitive: true,
				Description: "Workspace management API key (the operator's PlaceContext:Api:Key). " +
					"Sent as a bearer token. Falls back to the PLACECONTEXT_API_KEY environment variable.",
			},
		},
	}
}

func (p *placecontextProvider) Configure(ctx context.Context, req provider.ConfigureRequest, resp *provider.ConfigureResponse) {
	var config providerModel
	resp.Diagnostics.Append(req.Config.Get(ctx, &config)...)
	if resp.Diagnostics.HasError() {
		return
	}

	// Unknown values mean another (dependent) resource must resolve first.
	if config.Endpoint.IsUnknown() {
		resp.Diagnostics.AddAttributeError(pathRoot("endpoint"),
			"Unknown PlaceContext endpoint",
			"The provider cannot be configured with an unknown endpoint. Set it statically or via PLACECONTEXT_ENDPOINT.")
	}
	if config.APIKey.IsUnknown() {
		resp.Diagnostics.AddAttributeError(pathRoot("api_key"),
			"Unknown PlaceContext api_key",
			"The provider cannot be configured with an unknown api_key. Set it statically or via PLACECONTEXT_API_KEY.")
	}
	if resp.Diagnostics.HasError() {
		return
	}

	endpoint := strings.TrimSpace(os.Getenv("PLACECONTEXT_ENDPOINT"))
	if !config.Endpoint.IsNull() {
		endpoint = strings.TrimSpace(config.Endpoint.ValueString())
	}
	apiKey := os.Getenv("PLACECONTEXT_API_KEY")
	if !config.APIKey.IsNull() {
		apiKey = config.APIKey.ValueString()
	}

	if endpoint == "" {
		resp.Diagnostics.AddAttributeError(pathRoot("endpoint"),
			"Missing PlaceContext endpoint",
			"Set the provider `endpoint` argument or the PLACECONTEXT_ENDPOINT environment variable.")
	}
	if apiKey == "" {
		resp.Diagnostics.AddAttributeError(pathRoot("api_key"),
			"Missing PlaceContext api_key",
			"Set the provider `api_key` argument or the PLACECONTEXT_API_KEY environment variable.")
	}
	if resp.Diagnostics.HasError() {
		return
	}

	c := client.New(endpoint, apiKey)
	resp.ResourceData = c
	resp.DataSourceData = c
}

func (p *placecontextProvider) Resources(_ context.Context) []func() resource.Resource {
	return []func() resource.Resource{
		NewProjectResource,
		NewJobResource,
		NewScheduleResource,
	}
}

func (p *placecontextProvider) DataSources(_ context.Context) []func() datasource.DataSource {
	return nil
}
