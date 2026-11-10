package provider

import (
	"context"

	"github.com/hashicorp/terraform-plugin-framework-validators/listvalidator"
	"github.com/hashicorp/terraform-plugin-framework-validators/stringvalidator"
	"github.com/hashicorp/terraform-plugin-framework/attr"
	"github.com/hashicorp/terraform-plugin-framework/path"
	"github.com/hashicorp/terraform-plugin-framework/resource"
	"github.com/hashicorp/terraform-plugin-framework/resource/schema"
	"github.com/hashicorp/terraform-plugin-framework/resource/schema/planmodifier"
	"github.com/hashicorp/terraform-plugin-framework/resource/schema/stringplanmodifier"
	"github.com/hashicorp/terraform-plugin-framework/schema/validator"
	"github.com/hashicorp/terraform-plugin-framework/types"
)

var (
	_ resource.Resource                = (*jobResource)(nil)
	_ resource.ResourceWithConfigure   = (*jobResource)(nil)
	_ resource.ResourceWithImportState = (*jobResource)(nil)
)

func newJobResource() resource.Resource { return &jobResource{} }

type jobResource struct {
	client *Client
}

var jobFileObjectType = types.ObjectType{AttrTypes: map[string]attr.Type{
	"path":    types.StringType,
	"content": types.StringType,
}}

var jobParameterObjectType = types.ObjectType{AttrTypes: map[string]attr.Type{
	"name":     types.StringType,
	"label":    types.StringType,
	"required": types.BoolType,
	"type":     types.StringType,
	"options":  types.ListType{ElemType: types.StringType},
}}

type jobFileModel struct {
	Path    types.String `tfsdk:"path"`
	Content types.String `tfsdk:"content"`
}

type jobParameterModel struct {
	Name     types.String `tfsdk:"name"`
	Label    types.String `tfsdk:"label"`
	Required types.Bool   `tfsdk:"required"`
	Type     types.String `tfsdk:"type"`
	Options  types.List   `tfsdk:"options"`
}

type jobResourceModel struct {
	ID                 types.String `tfsdk:"id"`
	ProjectID          types.String `tfsdk:"project_id"`
	Name               types.String `tfsdk:"name"`
	Description        types.String `tfsdk:"description"`
	MapImage           types.String `tfsdk:"map_image"`
	MapRuntimeID       types.String `tfsdk:"map_runtime_id"`
	MapSource          types.String `tfsdk:"map_source"`
	MapEntrypoint      types.String `tfsdk:"map_entrypoint"`
	MapFiles           types.List   `tfsdk:"map_files"`
	InputPayloads      types.List   `tfsdk:"input_payloads"`
	MapEnv             types.Map    `tfsdk:"map_env"`
	ReduceImage        types.String `tfsdk:"reduce_image"`
	ReduceRuntimeID    types.String `tfsdk:"reduce_runtime_id"`
	ReduceSource       types.String `tfsdk:"reduce_source"`
	ReduceEntrypoint   types.String `tfsdk:"reduce_entrypoint"`
	ReduceFiles        types.List   `tfsdk:"reduce_files"`
	ReduceEnv          types.Map    `tfsdk:"reduce_env"`
	ConcurrencyLimit   types.Int64  `tfsdk:"concurrency_limit"`
	SuccessExitCodes   types.List   `tfsdk:"success_exit_codes"`
	PartialExitCodes   types.List   `tfsdk:"partial_exit_codes"`
	AllowNetworkEgress types.Bool   `tfsdk:"allow_network_egress"`
	AllowAPIInvocation types.Bool   `tfsdk:"allow_api_invocation"`
	Parameters         types.List   `tfsdk:"parameters"`
	PostJobActions     types.List   `tfsdk:"post_job_actions"`
	ReturnType         types.String `tfsdk:"return_type"`
	ReturnFileName     types.String `tfsdk:"return_file_name"`
	RetryCount         types.Int64  `tfsdk:"retry_count"`
	RetryDelaySeconds  types.Int64  `tfsdk:"retry_delay_seconds"`
	McpConnectionIDs   types.List   `tfsdk:"mcp_connection_ids"`
	MapSourceKind      types.String `tfsdk:"map_source_kind"`
	ReduceSourceKind   types.String `tfsdk:"reduce_source_kind"`
	CreatedAt          types.String `tfsdk:"created_at"`
	UpdatedAt          types.String `tfsdk:"updated_at"`
}

func (r *jobResource) Metadata(_ context.Context, req resource.MetadataRequest, resp *resource.MetadataResponse) {
	resp.TypeName = req.ProviderTypeName + "_job"
}

func (r *jobResource) Schema(_ context.Context, _ resource.SchemaRequest, resp *resource.SchemaResponse) {
	resp.Schema = schema.Schema{
		MarkdownDescription: "A PlaceContext job definition. Supply exactly one map workload: an " +
			"`map_image`, or `map_runtime_id` with `map_source`/`map_files`. The optional reduce step " +
			"follows the same rule; omit every `reduce_*` field for a map-only job.",
		Attributes: map[string]schema.Attribute{
			"id": schema.StringAttribute{
				Computed:            true,
				MarkdownDescription: "Job UUID.",
				PlanModifiers:       []planmodifier.String{stringplanmodifier.UseStateForUnknown()},
			},
			"project_id": schema.StringAttribute{
				Required:            true,
				MarkdownDescription: "Project that owns the job.",
				PlanModifiers:       []planmodifier.String{stringplanmodifier.RequiresReplace()},
			},
			"name": schema.StringAttribute{
				Required:            true,
				MarkdownDescription: "Job name.",
			},
			"description": schema.StringAttribute{
				Optional:            true,
				MarkdownDescription: "Optional description.",
			},
			"map_image": schema.StringAttribute{
				Optional:            true,
				MarkdownDescription: "Container image for the map step. Mutually exclusive with `map_source`/`map_files`.",
			},
			"map_runtime_id": schema.StringAttribute{
				Optional:            true,
				MarkdownDescription: "Runtime id for a code-based map step.",
			},
			"map_source": schema.StringAttribute{
				Optional:            true,
				MarkdownDescription: "Entrypoint source for a code-based map step.",
			},
			"map_entrypoint": schema.StringAttribute{
				Optional:            true,
				MarkdownDescription: "Entrypoint file/command for a code-based map step.",
			},
			"map_files": schema.ListNestedAttribute{
				Optional:            true,
				Computed:            true,
				MarkdownDescription: "Source files for a code-based map step.",
				NestedObject: schema.NestedAttributeObject{
					Attributes: map[string]schema.Attribute{
						"path":    schema.StringAttribute{Required: true, MarkdownDescription: "File path."},
						"content": schema.StringAttribute{Required: true, MarkdownDescription: "File contents."},
					},
				},
			},
			"input_payloads": schema.ListAttribute{
				Optional:            true,
				Computed:            true,
				ElementType:         types.StringType,
				MarkdownDescription: "Default shard input payloads.",
			},
			"map_env": schema.MapAttribute{
				Optional:            true,
				Computed:            true,
				ElementType:         types.StringType,
				MarkdownDescription: "Environment variables for the map step.",
			},
			"reduce_image": schema.StringAttribute{
				Optional:            true,
				MarkdownDescription: "Container image for the reduce step.",
			},
			"reduce_runtime_id": schema.StringAttribute{
				Optional:            true,
				MarkdownDescription: "Runtime id for a code-based reduce step.",
			},
			"reduce_source": schema.StringAttribute{
				Optional:            true,
				MarkdownDescription: "Entrypoint source for a code-based reduce step.",
			},
			"reduce_entrypoint": schema.StringAttribute{
				Optional:            true,
				MarkdownDescription: "Entrypoint file/command for a code-based reduce step.",
			},
			"reduce_files": schema.ListNestedAttribute{
				Optional:            true,
				Computed:            true,
				MarkdownDescription: "Source files for a code-based reduce step.",
				NestedObject: schema.NestedAttributeObject{
					Attributes: map[string]schema.Attribute{
						"path":    schema.StringAttribute{Required: true, MarkdownDescription: "File path."},
						"content": schema.StringAttribute{Required: true, MarkdownDescription: "File contents."},
					},
				},
			},
			"reduce_env": schema.MapAttribute{
				Optional:            true,
				Computed:            true,
				ElementType:         types.StringType,
				MarkdownDescription: "Environment variables for the reduce step.",
			},
			"concurrency_limit": schema.Int64Attribute{
				Optional:            true,
				Computed:            true,
				MarkdownDescription: "Maximum concurrent map shards. The API defaults to 4.",
			},
			"success_exit_codes": schema.ListAttribute{
				Optional:            true,
				Computed:            true,
				ElementType:         types.Int64Type,
				MarkdownDescription: "Exit codes treated as success. Defaults to `[0]`.",
			},
			"partial_exit_codes": schema.ListAttribute{
				Optional:            true,
				Computed:            true,
				ElementType:         types.Int64Type,
				MarkdownDescription: "Exit codes treated as partial success.",
			},
			"allow_network_egress": schema.BoolAttribute{
				Optional:            true,
				Computed:            true,
				MarkdownDescription: "Allow outbound network access from the job's containers. Defaults to false.",
			},
			"allow_api_invocation": schema.BoolAttribute{
				Optional:            true,
				Computed:            true,
				MarkdownDescription: "Allow the job to invoke the PlaceContext API. Defaults to false.",
			},
			"parameters": schema.ListNestedAttribute{
				Optional:            true,
				Computed:            true,
				MarkdownDescription: "Declared runtime inputs for the job.",
				NestedObject: schema.NestedAttributeObject{
					Attributes: map[string]schema.Attribute{
						"name":  schema.StringAttribute{Required: true, MarkdownDescription: "Parameter name."},
						"label": schema.StringAttribute{Optional: true, MarkdownDescription: "Prompt label."},
						"required": schema.BoolAttribute{
							Optional:            true,
							Computed:            true,
							MarkdownDescription: "Whether a value must be supplied. Defaults to true.",
						},
						"type": schema.StringAttribute{
							Optional:            true,
							Computed:            true,
							MarkdownDescription: "Input type. Defaults to `text`.",
							Validators: []validator.String{stringvalidator.OneOf(
								"text", "number", "date", "datetime-local", "time", "select", "checkbox", "file")},
						},
						"options": schema.ListAttribute{
							Optional:            true,
							ElementType:         types.StringType,
							MarkdownDescription: "Allowed values when `type = \"select\"`.",
						},
					},
				},
			},
			"post_job_actions": schema.ListAttribute{
				Optional:            true,
				Computed:            true,
				ElementType:         types.StringType,
				MarkdownDescription: "Actions run after the job completes: `HtmlReport`, `Chart`, `Csv`, `RawBundle`, `HtmlOutput`.",
				Validators: []validator.List{listvalidator.ValueStringsAre(stringvalidator.OneOf(
					"HtmlReport", "Chart", "Csv", "RawBundle", "HtmlOutput"))},
			},
			"return_type": schema.StringAttribute{
				Optional:            true,
				Computed:            true,
				MarkdownDescription: "How the result is rendered. Defaults to `Json`.",
				Validators: []validator.String{stringvalidator.OneOf(
					"Json", "Table", "Chart", "Html", "Csv", "Text", "Pdf", "Image", "Video")},
			},
			"return_file_name": schema.StringAttribute{
				Optional:            true,
				MarkdownDescription: "Expected `/out` file name for file return types (`Pdf`, `Image`, `Video`).",
			},
			"retry_count": schema.Int64Attribute{
				Optional:            true,
				Computed:            true,
				MarkdownDescription: "Automatic retry attempts on failure. Defaults to 0.",
			},
			"retry_delay_seconds": schema.Int64Attribute{
				Optional:            true,
				Computed:            true,
				MarkdownDescription: "Fixed delay between retries in seconds. Defaults to 0.",
			},
			"mcp_connection_ids": schema.ListAttribute{
				Optional:            true,
				Computed:            true,
				ElementType:         types.StringType,
				MarkdownDescription: "MCP connection IDs made available to the job.",
			},
			"map_source_kind": schema.StringAttribute{
				Computed:            true,
				MarkdownDescription: "`image` or `code`, as resolved by the server.",
			},
			"reduce_source_kind": schema.StringAttribute{
				Computed:            true,
				MarkdownDescription: "`image` or `code` when a reduce step exists, otherwise null.",
			},
			"created_at": schema.StringAttribute{
				Computed:            true,
				MarkdownDescription: "Creation timestamp (RFC 3339).",
				PlanModifiers:       []planmodifier.String{stringplanmodifier.UseStateForUnknown()},
			},
			"updated_at": schema.StringAttribute{
				Computed:            true,
				MarkdownDescription: "Last update timestamp (RFC 3339).",
			},
		},
	}
}

func (r *jobResource) Configure(_ context.Context, req resource.ConfigureRequest, resp *resource.ConfigureResponse) {
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

func (r *jobResource) Create(ctx context.Context, req resource.CreateRequest, resp *resource.CreateResponse) {
	var plan jobResourceModel
	resp.Diagnostics.Append(req.Plan.Get(ctx, &plan)...)
	if resp.Diagnostics.HasError() {
		return
	}

	body, err := plan.toRequest(ctx)
	if err != nil {
		resp.Diagnostics.AddError("Could not encode job definition", err.Error())
		return
	}
	job, err := r.client.CreateJob(ctx, plan.ProjectID.ValueString(), body)
	if err != nil {
		resp.Diagnostics.AddError("Could not create PlaceContext job", err.Error())
		return
	}
	resp.Diagnostics.Append(resp.State.Set(ctx, jobToModel(ctx, job))...)
}

func (r *jobResource) Read(ctx context.Context, req resource.ReadRequest, resp *resource.ReadResponse) {
	var state jobResourceModel
	resp.Diagnostics.Append(req.State.Get(ctx, &state)...)
	if resp.Diagnostics.HasError() {
		return
	}

	job, err := r.client.GetJob(ctx, state.ID.ValueString())
	if err != nil {
		if IsNotFound(err) {
			resp.State.RemoveResource(ctx)
			return
		}
		resp.Diagnostics.AddError("Could not read PlaceContext job", err.Error())
		return
	}
	resp.Diagnostics.Append(resp.State.Set(ctx, jobToModel(ctx, job))...)
}

func (r *jobResource) Update(ctx context.Context, req resource.UpdateRequest, resp *resource.UpdateResponse) {
	var plan jobResourceModel
	resp.Diagnostics.Append(req.Plan.Get(ctx, &plan)...)
	if resp.Diagnostics.HasError() {
		return
	}

	body, err := plan.toRequest(ctx)
	if err != nil {
		resp.Diagnostics.AddError("Could not encode job definition", err.Error())
		return
	}
	id := plan.ID.ValueString()
	job, err := r.client.UpdateJob(ctx, id, body)
	if err != nil {
		if IsNotFound(err) {
			resp.State.RemoveResource(ctx)
			return
		}
		resp.Diagnostics.AddError("Could not update PlaceContext job", err.Error())
		return
	}
	resp.Diagnostics.Append(resp.State.Set(ctx, jobToModel(ctx, job))...)
}

func (r *jobResource) Delete(ctx context.Context, req resource.DeleteRequest, resp *resource.DeleteResponse) {
	var state jobResourceModel
	resp.Diagnostics.Append(req.State.Get(ctx, &state)...)
	if resp.Diagnostics.HasError() {
		return
	}
	if err := r.client.DeleteJob(ctx, state.ID.ValueString()); err != nil && !IsNotFound(err) {
		resp.Diagnostics.AddError("Could not delete PlaceContext job", err.Error())
	}
}

func (r *jobResource) ImportState(ctx context.Context, req resource.ImportStateRequest, resp *resource.ImportStateResponse) {
	resource.ImportStatePassthroughID(ctx, path.Root("id"), req, resp)
}

func (m *jobResourceModel) toRequest(ctx context.Context) (JobRequest, error) {
	request := JobRequest{
		Name:               m.Name.ValueString(),
		Description:        stringPtr(m.Description),
		MapImage:           stringPtr(m.MapImage),
		MapRuntimeID:       stringPtr(m.MapRuntimeID),
		MapSource:          stringPtr(m.MapSource),
		MapEntrypoint:      stringPtr(m.MapEntrypoint),
		ReduceImage:        stringPtr(m.ReduceImage),
		ReduceRuntimeID:    stringPtr(m.ReduceRuntimeID),
		ReduceSource:       stringPtr(m.ReduceSource),
		ReduceEntrypoint:   stringPtr(m.ReduceEntrypoint),
		ConcurrencyLimit:   intPtr(m.ConcurrencyLimit),
		AllowNetworkEgress: boolPtr(m.AllowNetworkEgress),
		AllowAPIInvocation: boolPtr(m.AllowAPIInvocation),
		ReturnType:         stringPtr(m.ReturnType),
		ReturnFileName:     stringPtr(m.ReturnFileName),
		RetryCount:         intPtr(m.RetryCount),
		RetryDelaySeconds:  intPtr(m.RetryDelaySeconds),
	}

	var err error
	if request.MapFiles, err = jobFilesFromList(ctx, m.MapFiles); err != nil {
		return JobRequest{}, err
	}
	if request.ReduceFiles, err = jobFilesFromList(ctx, m.ReduceFiles); err != nil {
		return JobRequest{}, err
	}
	if request.InputPayloads, err = stringsFromList(ctx, m.InputPayloads); err != nil {
		return JobRequest{}, err
	}
	if request.MapEnv, err = stringsFromMap(ctx, m.MapEnv); err != nil {
		return JobRequest{}, err
	}
	if request.ReduceEnv, err = stringsFromMap(ctx, m.ReduceEnv); err != nil {
		return JobRequest{}, err
	}
	if request.SuccessExitCodes, err = intsFromList(ctx, m.SuccessExitCodes); err != nil {
		return JobRequest{}, err
	}
	if request.PartialExitCodes, err = intsFromList(ctx, m.PartialExitCodes); err != nil {
		return JobRequest{}, err
	}
	if request.PostJobActions, err = stringsFromList(ctx, m.PostJobActions); err != nil {
		return JobRequest{}, err
	}
	if request.McpConnectionIDs, err = stringsFromList(ctx, m.McpConnectionIDs); err != nil {
		return JobRequest{}, err
	}
	if request.Parameters, err = jobParametersFromList(ctx, m.Parameters); err != nil {
		return JobRequest{}, err
	}
	return request, nil
}

func jobFilesFromList(ctx context.Context, value types.List) ([]JobCodeFile, error) {
	if value.IsNull() || value.IsUnknown() {
		return nil, nil
	}
	var files []jobFileModel
	if diags := value.ElementsAs(ctx, &files, false); diags.HasError() {
		return nil, diagError(diags)
	}
	out := make([]JobCodeFile, len(files))
	for i, file := range files {
		out[i] = JobCodeFile{Path: file.Path.ValueString(), Content: file.Content.ValueString()}
	}
	return out, nil
}

func jobParametersFromList(ctx context.Context, value types.List) ([]JobParameter, error) {
	if value.IsNull() || value.IsUnknown() {
		return nil, nil
	}
	var parameters []jobParameterModel
	if diags := value.ElementsAs(ctx, &parameters, false); diags.HasError() {
		return nil, diagError(diags)
	}
	out := make([]JobParameter, len(parameters))
	for i, parameter := range parameters {
		out[i] = JobParameter{
			Name:     parameter.Name.ValueString(),
			Label:    stringPtr(parameter.Label),
			Required: boolPtr(parameter.Required),
			Type:     stringPtr(parameter.Type),
		}
		options, err := stringsFromList(ctx, parameter.Options)
		if err != nil {
			return nil, err
		}
		out[i].Options = options
	}
	return out, nil
}

func jobToModel(ctx context.Context, job *Job) *jobResourceModel {
	return &jobResourceModel{
		ID:                 types.StringValue(job.ID),
		ProjectID:          types.StringValue(job.ProjectID),
		Name:               types.StringValue(job.Name),
		Description:        stringValue(job.Description),
		MapImage:           stringValue(job.MapImage),
		MapRuntimeID:       stringValue(job.MapRuntimeID),
		MapSource:          stringValue(job.MapSource),
		MapEntrypoint:      stringValue(job.MapEntrypoint),
		MapFiles:           jobFilesToList(job.MapFiles),
		InputPayloads:      stringsToList(job.InputPayloads),
		MapEnv:             stringsToMap(job.MapEnv),
		ReduceImage:        stringValue(job.ReduceImage),
		ReduceRuntimeID:    stringValue(job.ReduceRuntimeID),
		ReduceSource:       stringValue(job.ReduceSource),
		ReduceEntrypoint:   stringValue(job.ReduceEntrypoint),
		ReduceFiles:        jobFilesToList(job.ReduceFiles),
		ReduceEnv:          stringsToMap(job.ReduceEnv),
		ConcurrencyLimit:   types.Int64Value(int64(job.ConcurrencyLimit)),
		SuccessExitCodes:   intsToList(job.SuccessExitCodes),
		PartialExitCodes:   intsToList(job.PartialExitCodes),
		AllowNetworkEgress: types.BoolValue(job.AllowNetworkEgress),
		AllowAPIInvocation: types.BoolValue(job.AllowAPIInvocation),
		Parameters:         jobParametersToList(job.Parameters),
		PostJobActions:     stringsToList(job.PostJobActions),
		ReturnType:         types.StringValue(job.ReturnType),
		ReturnFileName:     stringValue(job.ReturnFileName),
		RetryCount:         types.Int64Value(int64(job.RetryCount)),
		RetryDelaySeconds:  types.Int64Value(int64(job.RetryDelaySeconds)),
		McpConnectionIDs:   stringsToList(job.McpConnectionIDs),
		MapSourceKind:      types.StringValue(job.MapSourceKind),
		ReduceSourceKind:   stringValue(job.ReduceSourceKind),
		CreatedAt:          types.StringValue(job.CreatedAt),
		UpdatedAt:          types.StringValue(job.UpdatedAt),
	}
}

func jobFilesToList(files []JobCodeFile) types.List {
	if files == nil {
		return types.ListNull(jobFileObjectType)
	}
	elems := make([]attr.Value, len(files))
	for i, file := range files {
		elems[i] = types.ObjectValueMust(jobFileObjectType.AttrTypes, map[string]attr.Value{
			"path":    types.StringValue(file.Path),
			"content": types.StringValue(file.Content),
		})
	}
	return types.ListValueMust(jobFileObjectType, elems)
}

func jobParametersToList(parameters []JobParameter) types.List {
	if parameters == nil {
		return types.ListNull(jobParameterObjectType)
	}
	elems := make([]attr.Value, len(parameters))
	for i, parameter := range parameters {
		required := types.BoolValue(true)
		if parameter.Required != nil {
			required = types.BoolValue(*parameter.Required)
		}
		parameterType := types.StringValue("text")
		if parameter.Type != nil {
			parameterType = types.StringValue(*parameter.Type)
		}
		elems[i] = types.ObjectValueMust(jobParameterObjectType.AttrTypes, map[string]attr.Value{
			"name":     types.StringValue(parameter.Name),
			"label":    stringValue(parameter.Label),
			"required": required,
			"type":     parameterType,
			"options":  stringsToList(parameter.Options),
		})
	}
	return types.ListValueMust(jobParameterObjectType, elems)
}
