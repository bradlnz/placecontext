package provider

import (
	"context"
	"fmt"

	"github.com/hashicorp/terraform-plugin-framework-validators/stringvalidator"
	"github.com/hashicorp/terraform-plugin-framework/datasource"
	"github.com/hashicorp/terraform-plugin-framework/datasource/schema"
	"github.com/hashicorp/terraform-plugin-framework/path"
	"github.com/hashicorp/terraform-plugin-framework/schema/validator"
	"github.com/hashicorp/terraform-plugin-framework/types"
)

var (
	_ datasource.DataSource              = (*projectDataSource)(nil)
	_ datasource.DataSourceWithConfigure = (*projectDataSource)(nil)
)

func newProjectDataSource() datasource.DataSource { return &projectDataSource{} }

type projectDataSource struct {
	client *Client
}

type projectDataSourceModel struct {
	ID           types.String `tfsdk:"id"`
	Path         types.String `tfsdk:"path"`
	Name         types.String `tfsdk:"name"`
	Status       types.String `tfsdk:"status"`
	IsGraphified types.Bool   `tfsdk:"is_graphified"`
}

func (d *projectDataSource) Metadata(_ context.Context, req datasource.MetadataRequest, resp *datasource.MetadataResponse) {
	resp.TypeName = req.ProviderTypeName + "_project"
}

func (d *projectDataSource) Schema(_ context.Context, _ datasource.SchemaRequest, resp *datasource.SchemaResponse) {
	resp.Schema = schema.Schema{
		MarkdownDescription: "Look up a PlaceContext project by `id` or `path`.",
		Attributes: map[string]schema.Attribute{
			"id": schema.StringAttribute{
				Optional:            true,
				Computed:            true,
				MarkdownDescription: "Project UUID. Exactly one of `id` or `path` must be set.",
				Validators:          []validator.String{stringvalidator.ExactlyOneOf(path.MatchRoot("path"))},
			},
			"path": schema.StringAttribute{
				Optional:            true,
				Computed:            true,
				MarkdownDescription: "Project path. Exactly one of `id` or `path` must be set.",
			},
			"name": schema.StringAttribute{
				Computed:            true,
				MarkdownDescription: "Display name.",
			},
			"status": schema.StringAttribute{
				Computed:            true,
				MarkdownDescription: "Lifecycle status.",
			},
			"is_graphified": schema.BoolAttribute{
				Computed:            true,
				MarkdownDescription: "Whether the project's data graph has been built.",
			},
		},
	}
}

func (d *projectDataSource) Configure(_ context.Context, req datasource.ConfigureRequest, resp *datasource.ConfigureResponse) {
	if req.ProviderData == nil {
		return
	}
	client, ok := req.ProviderData.(*Client)
	if !ok {
		resp.Diagnostics.AddError("Unexpected provider data",
			"Expected *provider.Client, got a different type. This is a bug in the provider.")
		return
	}
	d.client = client
}

func (d *projectDataSource) Read(ctx context.Context, req datasource.ReadRequest, resp *datasource.ReadResponse) {
	var config projectDataSourceModel
	resp.Diagnostics.Append(req.Config.Get(ctx, &config)...)
	if resp.Diagnostics.HasError() {
		return
	}

	var project *Project
	if id := config.ID.ValueString(); id != "" {
		found, err := d.client.GetProject(ctx, id)
		if err != nil {
			resp.Diagnostics.AddError("Could not read PlaceContext project", err.Error())
			return
		}
		project = found
	} else {
		pathValue := config.Path.ValueString()
		projects, err := d.client.ListProjects(ctx)
		if err != nil {
			resp.Diagnostics.AddError("Could not list PlaceContext projects", err.Error())
			return
		}
		for i := range projects {
			if projects[i].Path == pathValue {
				project = &projects[i]
				break
			}
		}
		if project == nil {
			resp.Diagnostics.AddError("PlaceContext project not found",
				fmt.Sprintf("No project with path %q exists in this workspace.", pathValue))
			return
		}
	}

	resp.Diagnostics.Append(resp.State.Set(ctx, &projectDataSourceModel{
		ID:           types.StringValue(project.ID),
		Path:         types.StringValue(project.Path),
		Name:         types.StringValue(project.Name),
		Status:       types.StringValue(project.Status),
		IsGraphified: types.BoolValue(project.IsGraphified),
	})...)
}
