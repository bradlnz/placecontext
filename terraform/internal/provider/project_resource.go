package provider

import (
	"context"
	"errors"

	"github.com/bradlnz/terraform-provider-placecontext/internal/client"
	"github.com/hashicorp/terraform-plugin-framework/path"
	"github.com/hashicorp/terraform-plugin-framework/resource"
	"github.com/hashicorp/terraform-plugin-framework/resource/schema"
	"github.com/hashicorp/terraform-plugin-framework/resource/schema/planmodifier"
	"github.com/hashicorp/terraform-plugin-framework/resource/schema/stringplanmodifier"
	"github.com/hashicorp/terraform-plugin-framework/types"
)

var (
	_ resource.Resource                = (*projectResource)(nil)
	_ resource.ResourceWithConfigure   = (*projectResource)(nil)
	_ resource.ResourceWithImportState = (*projectResource)(nil)
)

// NewProjectResource constructs the placecontext_project resource.
func NewProjectResource() resource.Resource { return &projectResource{} }

type projectResource struct {
	client *client.Client
}

type projectModel struct {
	ID           types.String `tfsdk:"id"`
	Name         types.String `tfsdk:"name"`
	Path         types.String `tfsdk:"path"`
	Status       types.String `tfsdk:"status"`
	IsGraphified types.Bool   `tfsdk:"is_graphified"`
}

func (r *projectResource) Metadata(_ context.Context, req resource.MetadataRequest, resp *resource.MetadataResponse) {
	resp.TypeName = req.ProviderTypeName + "_project"
}

func (r *projectResource) Schema(_ context.Context, _ resource.SchemaRequest, resp *resource.SchemaResponse) {
	resp.Schema = schema.Schema{
		Description: "A PlaceContext project — a codebase under watch. Registration is idempotent by " +
			"path. NOTE: the API exposes no project delete; destroying this resource only removes it " +
			"from Terraform state (a warning is emitted).",
		Attributes: map[string]schema.Attribute{
			"id": schema.StringAttribute{
				Computed:      true,
				Description:   "Server-assigned project id (GUID).",
				PlanModifiers: []planmodifier.String{stringplanmodifier.UseStateForUnknown()},
			},
			"path": schema.StringAttribute{
				Required:    true,
				Description: "Filesystem path of the codebase. Changing it registers a different project.",
				PlanModifiers: []planmodifier.String{
					stringplanmodifier.RequiresReplace(),
				},
			},
			"name": schema.StringAttribute{
				Optional:      true,
				Computed:      true,
				Description:   "Display name. Defaults to the last path segment when omitted.",
				PlanModifiers: []planmodifier.String{stringplanmodifier.UseStateForUnknown()},
			},
			"status": schema.StringAttribute{
				Computed:      true,
				Description:   "Server-computed lifecycle status (e.g. Registered).",
				PlanModifiers: []planmodifier.String{stringplanmodifier.UseStateForUnknown()},
			},
			"is_graphified": schema.BoolAttribute{
				Computed:    true,
				Description: "Whether the project's knowledge graph has been built.",
			},
		},
	}
}

func (r *projectResource) Configure(_ context.Context, req resource.ConfigureRequest, resp *resource.ConfigureResponse) {
	r.client = clientFromProviderData(req.ProviderData, &resp.Diagnostics)
}

func (r *projectResource) Create(ctx context.Context, req resource.CreateRequest, resp *resource.CreateResponse) {
	var plan projectModel
	resp.Diagnostics.Append(req.Plan.Get(ctx, &plan)...)
	if resp.Diagnostics.HasError() {
		return
	}

	created, err := r.client.CreateProject(ctx, client.CreateProjectRequest{
		Path: plan.Path.ValueString(),
		Name: plan.Name.ValueString(),
	})
	if err != nil {
		resp.Diagnostics.AddError("Error creating project", err.Error())
		return
	}

	resp.Diagnostics.Append(resp.State.Set(ctx, projectToModel(created))...)
}

func (r *projectResource) Read(ctx context.Context, req resource.ReadRequest, resp *resource.ReadResponse) {
	var state projectModel
	resp.Diagnostics.Append(req.State.Get(ctx, &state)...)
	if resp.Diagnostics.HasError() {
		return
	}

	p, err := r.client.GetProject(ctx, state.ID.ValueString())
	if err != nil {
		if errors.Is(err, client.ErrNotFound) {
			resp.State.RemoveResource(ctx)
			return
		}
		resp.Diagnostics.AddError("Error reading project", err.Error())
		return
	}

	resp.Diagnostics.Append(resp.State.Set(ctx, projectToModel(p))...)
}

// Update: every mutable input is ForceNew, so a plan that reaches Update has no
// changed remote fields. Persist the plan as-is.
func (r *projectResource) Update(ctx context.Context, req resource.UpdateRequest, resp *resource.UpdateResponse) {
	var plan projectModel
	resp.Diagnostics.Append(req.Plan.Get(ctx, &plan)...)
	if resp.Diagnostics.HasError() {
		return
	}
	resp.Diagnostics.Append(resp.State.Set(ctx, &plan)...)
}

// Delete: the API has no project delete path. Remove from state and warn.
func (r *projectResource) Delete(_ context.Context, _ resource.DeleteRequest, resp *resource.DeleteResponse) {
	resp.Diagnostics.AddWarning(
		"Project not deleted remotely",
		"The PlaceContext management API exposes no project delete endpoint, so this project was "+
			"only removed from Terraform state. It still exists in the workspace.",
	)
}

func (r *projectResource) ImportState(ctx context.Context, req resource.ImportStateRequest, resp *resource.ImportStateResponse) {
	resource.ImportStatePassthroughID(ctx, path.Root("id"), req, resp)
}

func projectToModel(p *client.Project) projectModel {
	return projectModel{
		ID:           types.StringValue(p.ID),
		Name:         strValueOrNull(p.Name),
		Path:         types.StringValue(p.Path),
		Status:       strValueOrNull(p.Status),
		IsGraphified: types.BoolValue(p.IsGraphified),
	}
}
