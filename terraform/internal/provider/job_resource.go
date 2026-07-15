package provider

import (
	"context"
	"errors"

	"github.com/bradlnz/terraform-provider-placecontext/internal/client"
	"github.com/hashicorp/terraform-plugin-framework/attr"
	"github.com/hashicorp/terraform-plugin-framework/diag"
	"github.com/hashicorp/terraform-plugin-framework/path"
	"github.com/hashicorp/terraform-plugin-framework/resource"
	"github.com/hashicorp/terraform-plugin-framework/resource/schema"
	"github.com/hashicorp/terraform-plugin-framework/resource/schema/booldefault"
	"github.com/hashicorp/terraform-plugin-framework/resource/schema/int64default"
	"github.com/hashicorp/terraform-plugin-framework/resource/schema/listdefault"
	"github.com/hashicorp/terraform-plugin-framework/resource/schema/planmodifier"
	"github.com/hashicorp/terraform-plugin-framework/resource/schema/stringdefault"
	"github.com/hashicorp/terraform-plugin-framework/resource/schema/stringplanmodifier"
	"github.com/hashicorp/terraform-plugin-framework/types"
)

var (
	_ resource.Resource                = (*jobResource)(nil)
	_ resource.ResourceWithConfigure   = (*jobResource)(nil)
	_ resource.ResourceWithImportState = (*jobResource)(nil)
)

// NewJobResource constructs the placecontext_job resource.
func NewJobResource() resource.Resource { return &jobResource{} }

type jobResource struct {
	client *client.Client
}

// ---- object attribute types for nested blocks ----

var fileObjectType = types.ObjectType{AttrTypes: map[string]attr.Type{
	"path":    types.StringType,
	"content": types.StringType,
}}

var parameterObjectType = types.ObjectType{AttrTypes: map[string]attr.Type{
	"name":     types.StringType,
	"label":    types.StringType,
	"required": types.BoolType,
	"type":     types.StringType,
	"options":  types.ListType{ElemType: types.StringType},
}}

// ---- models ----

type jobModel struct {
	ID          types.String `tfsdk:"id"`
	ProjectID   types.String `tfsdk:"project_id"`
	Name        types.String `tfsdk:"name"`
	Description types.String `tfsdk:"description"`

	MapSourceKind types.String `tfsdk:"map_source_kind"`
	MapImage      types.String `tfsdk:"map_image"`
	MapRuntimeID  types.String `tfsdk:"map_runtime_id"`
	MapSource     types.String `tfsdk:"map_source"`
	MapEntrypoint types.String `tfsdk:"map_entrypoint"`
	MapFiles      types.List   `tfsdk:"map_files"`

	InputPayloads types.List `tfsdk:"input_payloads"`
	MapEnv        types.Map  `tfsdk:"map_env"`

	ReduceSourceKind types.String `tfsdk:"reduce_source_kind"`
	ReduceImage      types.String `tfsdk:"reduce_image"`
	ReduceRuntimeID  types.String `tfsdk:"reduce_runtime_id"`
	ReduceSource     types.String `tfsdk:"reduce_source"`
	ReduceEntrypoint types.String `tfsdk:"reduce_entrypoint"`
	ReduceFiles      types.List   `tfsdk:"reduce_files"`
	ReduceEnv        types.Map    `tfsdk:"reduce_env"`

	ConcurrencyLimit   types.Int64  `tfsdk:"concurrency_limit"`
	SuccessExitCodes   types.List   `tfsdk:"success_exit_codes"`
	PartialExitCodes   types.List   `tfsdk:"partial_exit_codes"`
	AllowNetworkEgress types.Bool   `tfsdk:"allow_network_egress"`
	Parameters         types.List   `tfsdk:"parameters"`
	PostJobActions     types.List   `tfsdk:"post_job_actions"`
	ReturnType         types.String `tfsdk:"return_type"`
	ReturnFileName     types.String `tfsdk:"return_file_name"`

	CreatedAt types.String `tfsdk:"created_at"`
	UpdatedAt types.String `tfsdk:"updated_at"`
}

// fileModel / parameterModel are decode targets for the nested blocks.
type fileModel struct {
	Path    types.String `tfsdk:"path"`
	Content types.String `tfsdk:"content"`
}

type parameterModel struct {
	Name     types.String `tfsdk:"name"`
	Label    types.String `tfsdk:"label"`
	Required types.Bool   `tfsdk:"required"`
	Type     types.String `tfsdk:"type"`
	Options  types.List   `tfsdk:"options"`
}

func (r *jobResource) Metadata(_ context.Context, req resource.MetadataRequest, resp *resource.MetadataResponse) {
	resp.TypeName = req.ProviderTypeName + "_job"
}

func (r *jobResource) Schema(_ context.Context, _ resource.SchemaRequest, resp *resource.SchemaResponse) {
	fileNested := schema.NestedAttributeObject{Attributes: map[string]schema.Attribute{
		"path":    schema.StringAttribute{Required: true, Description: "File path within the runtime work dir."},
		"content": schema.StringAttribute{Required: true, Description: "File contents."},
	}}

	resp.Schema = schema.Schema{
		Description: "A PlaceContext job: a generic map/reduce workload definition under a project. A " +
			"map step is required and a reduce step optional; each step is sourced from either a " +
			"pre-built container image or inline/multi-file code run by a named runtime — never both.",
		Attributes: map[string]schema.Attribute{
			"id": schema.StringAttribute{
				Computed:      true,
				Description:   "Server-assigned job id (GUID).",
				PlanModifiers: []planmodifier.String{stringplanmodifier.UseStateForUnknown()},
			},
			"project_id": schema.StringAttribute{
				Required:      true,
				Description:   "Id of the project this job belongs to. Changing it recreates the job.",
				PlanModifiers: []planmodifier.String{stringplanmodifier.RequiresReplace()},
			},
			"name": schema.StringAttribute{
				Required:    true,
				Description: "Job name.",
			},
			"description": schema.StringAttribute{
				Optional:    true,
				Description: "Optional human-readable description.",
			},

			// map step source
			"map_image": schema.StringAttribute{
				Optional:    true,
				Description: "Pre-built container image for the map step (mutually exclusive with map_runtime_id/code).",
			},
			"map_runtime_id": schema.StringAttribute{
				Optional:    true,
				Description: "Runtime id for a code-based map step, e.g. \"python\", \"node\".",
			},
			"map_source": schema.StringAttribute{
				Optional:    true,
				Description: "Single-file inline map source (legacy; prefer map_files).",
			},
			"map_entrypoint": schema.StringAttribute{
				Optional:    true,
				Description: "Entry filename within the runtime work dir for the map step.",
			},
			"map_files": schema.ListNestedAttribute{
				Optional:     true,
				Description:  "Multi-file inline source for the map step; supersedes map_source when set.",
				NestedObject: fileNested,
			},
			"map_source_kind": schema.StringAttribute{
				Computed:    true,
				Description: "Server-computed source kind of the map step (e.g. \"image\", \"code\").",
			},

			"input_payloads": schema.ListAttribute{
				Optional:    true,
				ElementType: types.StringType,
				Description: "One opaque input shard per entry (defaults to empty).",
			},
			"map_env": schema.MapAttribute{
				Optional:    true,
				ElementType: types.StringType,
				Description: "Opaque environment variables passed to the map container.",
			},

			// reduce step source (all optional; omit for no reduce step)
			"reduce_image": schema.StringAttribute{
				Optional:    true,
				Description: "Pre-built container image for the reduce step.",
			},
			"reduce_runtime_id": schema.StringAttribute{
				Optional:    true,
				Description: "Runtime id for a code-based reduce step.",
			},
			"reduce_source": schema.StringAttribute{
				Optional:    true,
				Description: "Single-file inline reduce source (legacy; prefer reduce_files).",
			},
			"reduce_entrypoint": schema.StringAttribute{
				Optional:    true,
				Description: "Entry filename for the reduce step.",
			},
			"reduce_files": schema.ListNestedAttribute{
				Optional:     true,
				Description:  "Multi-file inline source for the reduce step.",
				NestedObject: fileNested,
			},
			"reduce_env": schema.MapAttribute{
				Optional:    true,
				ElementType: types.StringType,
				Description: "Opaque environment variables passed to the reduce container.",
			},
			"reduce_source_kind": schema.StringAttribute{
				Computed:    true,
				Description: "Server-computed source kind of the reduce step, or null if there is no reduce step.",
			},

			"concurrency_limit": schema.Int64Attribute{
				Optional:    true,
				Computed:    true,
				Default:     int64default.StaticInt64(1),
				Description: "Maximum concurrent map shards (>= 1). Defaults to 1.",
			},
			"success_exit_codes": schema.ListAttribute{
				Optional:    true,
				Computed:    true,
				ElementType: types.Int64Type,
				Default: listdefault.StaticValue(
					types.ListValueMust(types.Int64Type, []attr.Value{types.Int64Value(0)}),
				),
				Description: "Container exit codes treated as success. Defaults to [0].",
			},
			"partial_exit_codes": schema.ListAttribute{
				Optional:    true,
				ElementType: types.Int64Type,
				Description: "Container exit codes treated as a partial success.",
			},
			"allow_network_egress": schema.BoolAttribute{
				Optional:    true,
				Computed:    true,
				Default:     booldefault.StaticBool(false),
				Description: "When false (default), containers run with --network none.",
			},
			"parameters": schema.ListNestedAttribute{
				Optional:    true,
				Description: "Inputs prompted before a manual run.",
				NestedObject: schema.NestedAttributeObject{Attributes: map[string]schema.Attribute{
					"name":     schema.StringAttribute{Required: true, Description: "Parameter name."},
					"label":    schema.StringAttribute{Optional: true, Description: "Display label."},
					"required": schema.BoolAttribute{Required: true, Description: "Whether the parameter must be supplied."},
					"type":     schema.StringAttribute{Required: true, Description: "Parameter type."},
					"options": schema.ListAttribute{
						Optional:    true,
						ElementType: types.StringType,
						Description: "Allowed values, for enumerated parameter types.",
					},
				}},
			},
			"post_job_actions": schema.ListAttribute{
				Optional:    true,
				ElementType: types.StringType,
				Description: "Artifacts to produce: any of HtmlReport, Chart, Csv, RawBundle, HtmlOutput.",
			},
			"return_type": schema.StringAttribute{
				Optional:    true,
				Computed:    true,
				Default:     stringdefault.StaticString("Json"),
				Description: "Job return type: one of Json, Table, Chart, Html, Csv, Text, Pdf, Image, Video. Defaults to Json.",
			},
			"return_file_name": schema.StringAttribute{
				Optional:    true,
				Description: "Expected /out filename for Pdf/Image/Video return types.",
			},

			"created_at": schema.StringAttribute{
				Computed:      true,
				Description:   "Creation timestamp (ISO-8601).",
				PlanModifiers: []planmodifier.String{stringplanmodifier.UseStateForUnknown()},
			},
			"updated_at": schema.StringAttribute{
				Computed:    true,
				Description: "Last-update timestamp (ISO-8601).",
			},
		},
	}
}

func (r *jobResource) Configure(_ context.Context, req resource.ConfigureRequest, resp *resource.ConfigureResponse) {
	r.client = clientFromProviderData(req.ProviderData, &resp.Diagnostics)
}

func (r *jobResource) Create(ctx context.Context, req resource.CreateRequest, resp *resource.CreateResponse) {
	var plan jobModel
	resp.Diagnostics.Append(req.Plan.Get(ctx, &plan)...)
	if resp.Diagnostics.HasError() {
		return
	}

	job := modelToJob(ctx, plan, &resp.Diagnostics)
	if resp.Diagnostics.HasError() {
		return
	}

	created, err := r.client.CreateJob(ctx, plan.ProjectID.ValueString(), job)
	if err != nil {
		resp.Diagnostics.AddError("Error creating job", err.Error())
		return
	}

	state := jobToModel(ctx, created, &resp.Diagnostics)
	if resp.Diagnostics.HasError() {
		return
	}
	resp.Diagnostics.Append(resp.State.Set(ctx, state)...)
}

func (r *jobResource) Read(ctx context.Context, req resource.ReadRequest, resp *resource.ReadResponse) {
	var st jobModel
	resp.Diagnostics.Append(req.State.Get(ctx, &st)...)
	if resp.Diagnostics.HasError() {
		return
	}

	j, err := r.client.GetJob(ctx, st.ID.ValueString())
	if err != nil {
		if errors.Is(err, client.ErrNotFound) {
			resp.State.RemoveResource(ctx)
			return
		}
		resp.Diagnostics.AddError("Error reading job", err.Error())
		return
	}

	state := jobToModel(ctx, j, &resp.Diagnostics)
	if resp.Diagnostics.HasError() {
		return
	}
	resp.Diagnostics.Append(resp.State.Set(ctx, state)...)
}

func (r *jobResource) Update(ctx context.Context, req resource.UpdateRequest, resp *resource.UpdateResponse) {
	var plan jobModel
	resp.Diagnostics.Append(req.Plan.Get(ctx, &plan)...)
	if resp.Diagnostics.HasError() {
		return
	}

	job := modelToJob(ctx, plan, &resp.Diagnostics)
	if resp.Diagnostics.HasError() {
		return
	}

	updated, err := r.client.UpdateJob(ctx, plan.ID.ValueString(), job)
	if err != nil {
		resp.Diagnostics.AddError("Error updating job", err.Error())
		return
	}

	state := jobToModel(ctx, updated, &resp.Diagnostics)
	if resp.Diagnostics.HasError() {
		return
	}
	resp.Diagnostics.Append(resp.State.Set(ctx, state)...)
}

func (r *jobResource) Delete(ctx context.Context, req resource.DeleteRequest, resp *resource.DeleteResponse) {
	var st jobModel
	resp.Diagnostics.Append(req.State.Get(ctx, &st)...)
	if resp.Diagnostics.HasError() {
		return
	}

	err := r.client.DeleteJob(ctx, st.ID.ValueString())
	if err != nil && !errors.Is(err, client.ErrNotFound) {
		resp.Diagnostics.AddError("Error deleting job", err.Error())
	}
}

func (r *jobResource) ImportState(ctx context.Context, req resource.ImportStateRequest, resp *resource.ImportStateResponse) {
	resource.ImportStatePassthroughID(ctx, path.Root("id"), req, resp)
}

// ---- model <-> DTO conversion ----

func modelToJob(ctx context.Context, m jobModel, diags *diag.Diagnostics) client.Job {
	return client.Job{
		Name:               m.Name.ValueString(),
		Description:        strPtr(m.Description),
		MapImage:           strPtr(m.MapImage),
		MapRuntimeID:       strPtr(m.MapRuntimeID),
		MapSource:          strPtr(m.MapSource),
		MapEntrypoint:      strPtr(m.MapEntrypoint),
		MapFiles:           filesFromTF(ctx, m.MapFiles, diags),
		InputPayloads:      stringsFromTF(ctx, m.InputPayloads, diags),
		MapEnv:             mapFromTF(ctx, m.MapEnv, diags),
		ReduceImage:        strPtr(m.ReduceImage),
		ReduceRuntimeID:    strPtr(m.ReduceRuntimeID),
		ReduceSource:       strPtr(m.ReduceSource),
		ReduceEntrypoint:   strPtr(m.ReduceEntrypoint),
		ReduceFiles:        filesFromTF(ctx, m.ReduceFiles, diags),
		ReduceEnv:          mapFromTF(ctx, m.ReduceEnv, diags),
		ConcurrencyLimit:   int(m.ConcurrencyLimit.ValueInt64()),
		SuccessExitCodes:   intsFromTF(ctx, m.SuccessExitCodes, diags),
		PartialExitCodes:   intsFromTF(ctx, m.PartialExitCodes, diags),
		AllowNetworkEgress: m.AllowNetworkEgress.ValueBool(),
		Parameters:         paramsFromTF(ctx, m.Parameters, diags),
		PostJobActions:     stringsFromTF(ctx, m.PostJobActions, diags),
		ReturnType:         m.ReturnType.ValueString(),
		ReturnFileName:     strPtr(m.ReturnFileName),
	}
}

func jobToModel(ctx context.Context, j *client.Job, diags *diag.Diagnostics) jobModel {
	return jobModel{
		ID:                 types.StringValue(j.ID),
		ProjectID:          types.StringValue(j.ProjectID),
		Name:               types.StringValue(j.Name),
		Description:        strFromPtr(j.Description),
		MapSourceKind:      strFromPtr(j.MapSourceKind),
		MapImage:           strFromPtr(j.MapImage),
		MapRuntimeID:       strFromPtr(j.MapRuntimeID),
		MapSource:          strFromPtr(j.MapSource),
		MapEntrypoint:      strFromPtr(j.MapEntrypoint),
		MapFiles:           filesToTF(ctx, j.MapFiles, diags),
		InputPayloads:      stringsToTF(ctx, j.InputPayloads, diags),
		MapEnv:             mapToTF(ctx, j.MapEnv, diags),
		ReduceSourceKind:   strFromPtr(j.ReduceSourceKind),
		ReduceImage:        strFromPtr(j.ReduceImage),
		ReduceRuntimeID:    strFromPtr(j.ReduceRuntimeID),
		ReduceSource:       strFromPtr(j.ReduceSource),
		ReduceEntrypoint:   strFromPtr(j.ReduceEntrypoint),
		ReduceFiles:        filesToTF(ctx, j.ReduceFiles, diags),
		ReduceEnv:          mapToTF(ctx, j.ReduceEnv, diags),
		ConcurrencyLimit:   types.Int64Value(int64(j.ConcurrencyLimit)),
		SuccessExitCodes:   intsToTF(j.SuccessExitCodes),
		PartialExitCodes:   intsToTF(j.PartialExitCodes),
		AllowNetworkEgress: types.BoolValue(j.AllowNetworkEgress),
		Parameters:         paramsToTF(ctx, j.Parameters, diags),
		PostJobActions:     stringsToTF(ctx, j.PostJobActions, diags),
		ReturnType:         types.StringValue(j.ReturnType),
		ReturnFileName:     strFromPtr(j.ReturnFileName),
		CreatedAt:          strValueOrNull(j.CreatedAt),
		UpdatedAt:          strValueOrNull(j.UpdatedAt),
	}
}

// ---- list/map/nested helpers (null when empty, so optional fields round-trip) ----

func stringsFromTF(ctx context.Context, l types.List, diags *diag.Diagnostics) []string {
	if l.IsNull() || l.IsUnknown() {
		return nil
	}
	var out []string
	diags.Append(l.ElementsAs(ctx, &out, false)...)
	return out
}

func stringsToTF(ctx context.Context, vals []string, diags *diag.Diagnostics) types.List {
	if len(vals) == 0 {
		return types.ListNull(types.StringType)
	}
	l, d := types.ListValueFrom(ctx, types.StringType, vals)
	diags.Append(d...)
	return l
}

func intsFromTF(ctx context.Context, l types.List, diags *diag.Diagnostics) []int {
	if l.IsNull() || l.IsUnknown() {
		return nil
	}
	var raw []int64
	diags.Append(l.ElementsAs(ctx, &raw, false)...)
	out := make([]int, len(raw))
	for i, v := range raw {
		out[i] = int(v)
	}
	return out
}

func intsToTF(vals []int) types.List {
	if len(vals) == 0 {
		return types.ListNull(types.Int64Type)
	}
	elems := make([]attr.Value, len(vals))
	for i, v := range vals {
		elems[i] = types.Int64Value(int64(v))
	}
	return types.ListValueMust(types.Int64Type, elems)
}

func mapFromTF(ctx context.Context, m types.Map, diags *diag.Diagnostics) map[string]string {
	if m.IsNull() || m.IsUnknown() {
		return nil
	}
	out := map[string]string{}
	diags.Append(m.ElementsAs(ctx, &out, false)...)
	return out
}

func mapToTF(ctx context.Context, vals map[string]string, diags *diag.Diagnostics) types.Map {
	if len(vals) == 0 {
		return types.MapNull(types.StringType)
	}
	m, d := types.MapValueFrom(ctx, types.StringType, vals)
	diags.Append(d...)
	return m
}

func filesFromTF(ctx context.Context, l types.List, diags *diag.Diagnostics) []client.FileEntry {
	if l.IsNull() || l.IsUnknown() {
		return nil
	}
	var fm []fileModel
	diags.Append(l.ElementsAs(ctx, &fm, false)...)
	if diags.HasError() {
		return nil
	}
	out := make([]client.FileEntry, len(fm))
	for i, f := range fm {
		out[i] = client.FileEntry{Path: f.Path.ValueString(), Content: f.Content.ValueString()}
	}
	return out
}

func filesToTF(ctx context.Context, files []client.FileEntry, diags *diag.Diagnostics) types.List {
	if len(files) == 0 {
		return types.ListNull(fileObjectType)
	}
	fm := make([]fileModel, len(files))
	for i, f := range files {
		fm[i] = fileModel{Path: types.StringValue(f.Path), Content: types.StringValue(f.Content)}
	}
	l, d := types.ListValueFrom(ctx, fileObjectType, fm)
	diags.Append(d...)
	return l
}

func paramsFromTF(ctx context.Context, l types.List, diags *diag.Diagnostics) []client.JobParameter {
	if l.IsNull() || l.IsUnknown() {
		return nil
	}
	var pm []parameterModel
	diags.Append(l.ElementsAs(ctx, &pm, false)...)
	if diags.HasError() {
		return nil
	}
	out := make([]client.JobParameter, len(pm))
	for i, p := range pm {
		out[i] = client.JobParameter{
			Name:     p.Name.ValueString(),
			Label:    strPtr(p.Label),
			Required: p.Required.ValueBool(),
			Type:     p.Type.ValueString(),
			Options:  stringsFromTF(ctx, p.Options, diags),
		}
	}
	return out
}

func paramsToTF(ctx context.Context, params []client.JobParameter, diags *diag.Diagnostics) types.List {
	if len(params) == 0 {
		return types.ListNull(parameterObjectType)
	}
	pm := make([]parameterModel, len(params))
	for i, p := range params {
		pm[i] = parameterModel{
			Name:     types.StringValue(p.Name),
			Label:    strFromPtr(p.Label),
			Required: types.BoolValue(p.Required),
			Type:     types.StringValue(p.Type),
			Options:  stringsToTF(ctx, p.Options, diags),
		}
	}
	l, d := types.ListValueFrom(ctx, parameterObjectType, pm)
	diags.Append(d...)
	return l
}
