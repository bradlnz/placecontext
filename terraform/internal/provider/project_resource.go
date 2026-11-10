package provider

import (
	"context"

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

func newProjectResource() resource.Resource { return &projectResource{} }

// projectResource manages a PlaceContext project.
//
// The management API can create and read projects but cannot rename or delete
// them, so `path` forces replacement and Delete only removes the project from
// Terraform state (with a warning) — the project itself is left untouched.
type projectResource struct {
	client *Client
}

type projectResourceModel struct {
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
		MarkdownDescription: "A PlaceContext project. Projects are created by path and are idempotent: " +
			"re-applying a path returns the existing project. The management API has no rename or delete " +
			"endpoint, so `path` forces replacement and destroying this resource only removes it from " +
			"Terraform state.",
		Attributes: map[string]schema.Attribute{
			"id": schema.StringAttribute{
				Computed:            true,
				MarkdownDescription: "Project UUID.",
				PlanModifiers:       []planmodifier.String{stringplanmodifier.UseStateForUnknown()},
			},
			"path": schema.StringAttribute{
				Required:            true,
				MarkdownDescription: "Project path (for example `data/revenue`). Identifies the project and is idempotent on create.",
				PlanModifiers:       []planmodifier.String{stringplanmodifier.RequiresReplace()},
			},
			"name": schema.StringAttribute{
				Optional:            true,
				Computed:            true,
				MarkdownDescription: "Display name. When omitted the API derives it from the path. The API cannot rename a project, so changing this value is rejected at apply time.",
			},
			"status": schema.StringAttribute{
				Computed:            true,
				MarkdownDescription: "Lifecycle status: `Discovered`, `Registered`, `Graphified` or `Archived`.",
			},
			"is_graphified": schema.BoolAttribute{
				Computed:            true,
				MarkdownDescription: "Whether the project's data graph has been built.",
			},
		},
	}
}

func (r *projectResource) Configure(_ context.Context, req resource.ConfigureRequest, resp *resource.ConfigureResponse) {
	if req.ProviderData == nil {
		return
	}
	client, ok := req.ProviderData.(*Client)
	if !ok {
		resp.Diagnostics.AddError("Unexpected provider data",
			"Expected *provider.Client, got a different type. This is a bug in the provider.")
		return
	}
	r.client = client
}

func (r *projectResource) Create(ctx context.Context, req resource.CreateRequest, resp *resource.CreateResponse) {
	var plan projectResourceModel
	resp.Diagnostics.Append(req.Plan.Get(ctx, &plan)...)
	if resp.Diagnostics.HasError() {
		return
	}

	project, err := r.client.CreateProject(ctx, plan.Path.ValueString(), stringPtr(plan.Name))
	if err != nil {
		resp.Diagnostics.AddError("Could not create PlaceContext project", err.Error())
		return
	}
	resp.Diagnostics.Append(resp.State.Set(ctx, projectToModel(project))...)
}

func (r *projectResource) Read(ctx context.Context, req resource.ReadRequest, resp *resource.ReadResponse) {
	var state projectResourceModel
	resp.Diagnostics.Append(req.State.Get(ctx, &state)...)
	if resp.Diagnostics.HasError() {
		return
	}

	project, err := r.client.GetProject(ctx, state.ID.ValueString())
	if err != nil {
		if IsNotFound(err) {
			resp.State.RemoveResource(ctx)
			return
		}
		resp.Diagnostics.AddError("Could not read PlaceContext project", err.Error())
		return
	}
	resp.Diagnostics.Append(resp.State.Set(ctx, projectToModel(project))...)
}

func (r *projectResource) Update(ctx context.Context, req resource.UpdateRequest, resp *resource.UpdateResponse) {
	var plan, state projectResourceModel
	resp.Diagnostics.Append(req.Plan.Get(ctx, &plan)...)
	resp.Diagnostics.Append(req.State.Get(ctx, &state)...)
	if resp.Diagnostics.HasError() {
		return
	}

	if plan.Name.ValueString() != state.Name.ValueString() {
		resp.Diagnostics.AddError(
			"PlaceContext projects cannot be renamed",
			"The management API has no project update endpoint, so a project's display name is fixed at "+
				"creation. Rename it in the portal or create a new project under a different path.",
		)
		return
	}

	// Nothing else is mutable; refresh so computed fields stay accurate.
	project, err := r.client.GetProject(ctx, state.ID.ValueString())
	if err != nil {
		if IsNotFound(err) {
			resp.State.RemoveResource(ctx)
			return
		}
		resp.Diagnostics.AddError("Could not refresh PlaceContext project", err.Error())
		return
	}
	resp.Diagnostics.Append(resp.State.Set(ctx, projectToModel(project))...)
}

func (r *projectResource) Delete(ctx context.Context, req resource.DeleteRequest, resp *resource.DeleteResponse) {
	resp.Diagnostics.AddWarning(
		"PlaceContext project left in place",
		"The management API has no project delete endpoint. The project was removed from Terraform state "+
			"but still exists in the workspace.",
	)
	resp.State.RemoveResource(ctx)
}

func (r *projectResource) ImportState(ctx context.Context, req resource.ImportStateRequest, resp *resource.ImportStateResponse) {
	resource.ImportStatePassthroughID(ctx, path.Root("id"), req, resp)
}

func projectToModel(project *Project) *projectResourceModel {
	return &projectResourceModel{
		ID:           types.StringValue(project.ID),
		Name:         types.StringValue(project.Name),
		Path:         types.StringValue(project.Path),
		Status:       types.StringValue(project.Status),
		IsGraphified: types.BoolValue(project.IsGraphified),
	}
}
