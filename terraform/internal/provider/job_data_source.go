package provider

import (
	"context"

	"github.com/hashicorp/terraform-plugin-framework/datasource"
	"github.com/hashicorp/terraform-plugin-framework/datasource/schema"
	"github.com/hashicorp/terraform-plugin-framework/types"
)

var (
	_ datasource.DataSource              = (*jobDataSource)(nil)
	_ datasource.DataSourceWithConfigure = (*jobDataSource)(nil)
)

func newJobDataSource() datasource.DataSource { return &jobDataSource{} }

type jobDataSource struct {
	client *Client
}

func (d *jobDataSource) Metadata(_ context.Context, req datasource.MetadataRequest, resp *datasource.MetadataResponse) {
	resp.TypeName = req.ProviderTypeName + "_job"
}

func (d *jobDataSource) Schema(_ context.Context, _ datasource.SchemaRequest, resp *datasource.SchemaResponse) {
	resp.Schema = schema.Schema{
		MarkdownDescription: "Look up a PlaceContext job definition by `id`.",
		Attributes: map[string]schema.Attribute{
			"id":              schema.StringAttribute{Required: true, MarkdownDescription: "Job UUID."},
			"project_id":      schema.StringAttribute{Computed: true, MarkdownDescription: "Owning project."},
			"name":            schema.StringAttribute{Computed: true, MarkdownDescription: "Job name."},
			"description":     schema.StringAttribute{Computed: true, MarkdownDescription: "Description."},
			"map_source_kind": schema.StringAttribute{Computed: true, MarkdownDescription: "Resolved map workload kind."},
			"map_image":       schema.StringAttribute{Computed: true, MarkdownDescription: "Map step image."},
			"map_runtime_id":  schema.StringAttribute{Computed: true, MarkdownDescription: "Map step runtime id."},
			"map_source":      schema.StringAttribute{Computed: true, MarkdownDescription: "Map step source."},
			"map_entrypoint":  schema.StringAttribute{Computed: true, MarkdownDescription: "Map step entrypoint."},
			"map_files": schema.ListNestedAttribute{
				Computed: true,
				NestedObject: schema.NestedAttributeObject{
					Attributes: map[string]schema.Attribute{
						"path":    schema.StringAttribute{Computed: true, MarkdownDescription: "File path."},
						"content": schema.StringAttribute{Computed: true, MarkdownDescription: "File contents."},
					},
				},
			},
			"input_payloads":     schema.ListAttribute{Computed: true, ElementType: types.StringType, MarkdownDescription: "Default shard payloads."},
			"map_env":            schema.MapAttribute{Computed: true, ElementType: types.StringType, MarkdownDescription: "Map step environment."},
			"reduce_source_kind": schema.StringAttribute{Computed: true, MarkdownDescription: "Resolved reduce workload kind."},
			"reduce_image":       schema.StringAttribute{Computed: true, MarkdownDescription: "Reduce step image."},
			"reduce_runtime_id":  schema.StringAttribute{Computed: true, MarkdownDescription: "Reduce step runtime id."},
			"reduce_source":      schema.StringAttribute{Computed: true, MarkdownDescription: "Reduce step source."},
			"reduce_entrypoint":  schema.StringAttribute{Computed: true, MarkdownDescription: "Reduce step entrypoint."},
			"reduce_files": schema.ListNestedAttribute{
				Computed: true,
				NestedObject: schema.NestedAttributeObject{
					Attributes: map[string]schema.Attribute{
						"path":    schema.StringAttribute{Computed: true, MarkdownDescription: "File path."},
						"content": schema.StringAttribute{Computed: true, MarkdownDescription: "File contents."},
					},
				},
			},
			"reduce_env":           schema.MapAttribute{Computed: true, ElementType: types.StringType, MarkdownDescription: "Reduce step environment."},
			"concurrency_limit":    schema.Int64Attribute{Computed: true, MarkdownDescription: "Maximum concurrent map shards."},
			"success_exit_codes":   schema.ListAttribute{Computed: true, ElementType: types.Int64Type, MarkdownDescription: "Success exit codes."},
			"partial_exit_codes":   schema.ListAttribute{Computed: true, ElementType: types.Int64Type, MarkdownDescription: "Partial-success exit codes."},
			"allow_network_egress": schema.BoolAttribute{Computed: true, MarkdownDescription: "Whether outbound network access is allowed."},
			"allow_api_invocation": schema.BoolAttribute{Computed: true, MarkdownDescription: "Whether API invocation is allowed."},
			"parameters": schema.ListNestedAttribute{
				Computed: true,
				NestedObject: schema.NestedAttributeObject{
					Attributes: map[string]schema.Attribute{
						"name":     schema.StringAttribute{Computed: true, MarkdownDescription: "Parameter name."},
						"label":    schema.StringAttribute{Computed: true, MarkdownDescription: "Prompt label."},
						"required": schema.BoolAttribute{Computed: true, MarkdownDescription: "Whether a value is required."},
						"type":     schema.StringAttribute{Computed: true, MarkdownDescription: "Input type."},
						"options":  schema.ListAttribute{Computed: true, ElementType: types.StringType, MarkdownDescription: "Allowed values."},
					},
				},
			},
			"post_job_actions":    schema.ListAttribute{Computed: true, ElementType: types.StringType, MarkdownDescription: "Post-job actions."},
			"return_type":         schema.StringAttribute{Computed: true, MarkdownDescription: "Result render type."},
			"return_file_name":    schema.StringAttribute{Computed: true, MarkdownDescription: "Expected output file name."},
			"retry_count":         schema.Int64Attribute{Computed: true, MarkdownDescription: "Retry attempts."},
			"retry_delay_seconds": schema.Int64Attribute{Computed: true, MarkdownDescription: "Retry delay in seconds."},
			"mcp_connection_ids":  schema.ListAttribute{Computed: true, ElementType: types.StringType, MarkdownDescription: "MCP connection IDs."},
			"created_at":          schema.StringAttribute{Computed: true, MarkdownDescription: "Creation timestamp."},
			"updated_at":          schema.StringAttribute{Computed: true, MarkdownDescription: "Last update timestamp."},
		},
	}
}

func (d *jobDataSource) Configure(_ context.Context, req datasource.ConfigureRequest, resp *datasource.ConfigureResponse) {
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

func (d *jobDataSource) Read(ctx context.Context, req datasource.ReadRequest, resp *datasource.ReadResponse) {
	var config jobResourceModel
	resp.Diagnostics.Append(req.Config.Get(ctx, &config)...)
	if resp.Diagnostics.HasError() {
		return
	}

	job, err := d.client.GetJob(ctx, config.ID.ValueString())
	if err != nil {
		resp.Diagnostics.AddError("Could not read PlaceContext job", err.Error())
		return
	}
	resp.Diagnostics.Append(resp.State.Set(ctx, jobToModel(ctx, job))...)
}
