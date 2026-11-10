package provider

import (
	"context"
	"os"

	"github.com/hashicorp/terraform-plugin-framework/datasource"
	"github.com/hashicorp/terraform-plugin-framework/provider"
	"github.com/hashicorp/terraform-plugin-framework/provider/schema"
	"github.com/hashicorp/terraform-plugin-framework/resource"
	"github.com/hashicorp/terraform-plugin-framework/types"
)

var _ provider.Provider = (*placeContextProvider)(nil)

// placeContextProvider configures the management API client shared by every
// resource and data source.
type placeContextProvider struct {
	version string
}

// New returns the provider factory used by main and by tests.
func New(version string) func() provider.Provider {
	return func() provider.Provider {
		return &placeContextProvider{version: version}
	}
}

type providerModel struct {
	BaseURL types.String `tfsdk:"base_url"`
	APIKey  types.String `tfsdk:"api_key"`
}

func (p *placeContextProvider) Metadata(_ context.Context, _ provider.MetadataRequest, resp *provider.MetadataResponse) {
	resp.TypeName = "placecontext"
	resp.Version = p.version
}

func (p *placeContextProvider) Schema(_ context.Context, _ provider.SchemaRequest, resp *provider.SchemaResponse) {
	resp.Schema = schema.Schema{
		MarkdownDescription: "Manage PlaceContext projects, jobs and schedules as code through the " +
			"workspace management API (`/api/v1`). The API key is scoped to one workspace: point " +
			"`base_url` at that workspace's origin.",
		Attributes: map[string]schema.Attribute{
			"base_url": schema.StringAttribute{
				Optional: true,
				MarkdownDescription: "Workspace origin, for example `https://acme.placecontext.io` for a " +
					"managed workspace or `http://localhost:7700` for a self-hosted one. May also be set " +
					"with the `PLACECONTEXT_URL` environment variable.",
			},
			"api_key": schema.StringAttribute{
				Optional:  true,
				Sensitive: true,
				MarkdownDescription: "Management API key sent as `Authorization: Bearer <key>`. May also be " +
					"set with the `PLACECONTEXT_API_KEY` environment variable.",
			},
		},
	}
}

func (p *placeContextProvider) Configure(ctx context.Context, req provider.ConfigureRequest, resp *provider.ConfigureResponse) {
	var config providerModel
	resp.Diagnostics.Append(req.Config.Get(ctx, &config)...)
	if resp.Diagnostics.HasError() {
		return
	}

	baseURL := config.BaseURL.ValueString()
	if baseURL == "" {
		baseURL = os.Getenv("PLACECONTEXT_URL")
	}
	apiKey := config.APIKey.ValueString()
	if apiKey == "" {
		apiKey = os.Getenv("PLACECONTEXT_API_KEY")
	}

	if baseURL == "" {
		resp.Diagnostics.AddError(
			"Missing PlaceContext base URL",
			"Set the provider's base_url argument or the PLACECONTEXT_URL environment variable "+
				"(for example https://acme.placecontext.io).",
		)
	}
	if apiKey == "" {
		resp.Diagnostics.AddError(
			"Missing PlaceContext API key",
			"Set the provider's api_key argument or the PLACECONTEXT_API_KEY environment variable. "+
				"The key must be configured as PlaceContext:Api:Key on the workspace host.",
		)
	}
	if resp.Diagnostics.HasError() {
		return
	}

	client := NewClient(baseURL, apiKey)
	resp.ResourceData = client
	resp.DataSourceData = client
}

func (p *placeContextProvider) Resources(_ context.Context) []func() resource.Resource {
	return []func() resource.Resource{
		newProjectResource,
		newJobResource,
		newScheduleResource,
	}
}

func (p *placeContextProvider) DataSources(_ context.Context) []func() datasource.DataSource {
	return []func() datasource.DataSource{
		newProjectDataSource,
		newJobDataSource,
	}
}
