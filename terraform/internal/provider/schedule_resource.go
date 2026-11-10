package provider

import (
	"context"

	"github.com/hashicorp/terraform-plugin-framework-validators/stringvalidator"
	"github.com/hashicorp/terraform-plugin-framework/path"
	"github.com/hashicorp/terraform-plugin-framework/resource"
	"github.com/hashicorp/terraform-plugin-framework/resource/schema"
	"github.com/hashicorp/terraform-plugin-framework/resource/schema/planmodifier"
	"github.com/hashicorp/terraform-plugin-framework/resource/schema/stringplanmodifier"
	"github.com/hashicorp/terraform-plugin-framework/schema/validator"
	"github.com/hashicorp/terraform-plugin-framework/types"
)

var (
	_ resource.Resource                = (*scheduleResource)(nil)
	_ resource.ResourceWithConfigure   = (*scheduleResource)(nil)
	_ resource.ResourceWithImportState = (*scheduleResource)(nil)
)

func newScheduleResource() resource.Resource { return &scheduleResource{} }

// scheduleResource manages a cron schedule or event trigger for a job.
type scheduleResource struct {
	client *Client
}

type scheduleResourceModel struct {
	ID             types.String `tfsdk:"id"`
	ProjectID      types.String `tfsdk:"project_id"`
	JobID          types.String `tfsdk:"job_id"`
	Name           types.String `tfsdk:"name"`
	Kind           types.String `tfsdk:"kind"`
	CronExpression types.String `tfsdk:"cron_expression"`
	EventName      types.String `tfsdk:"event_name"`
	Enabled        types.Bool   `tfsdk:"enabled"`
	NextRunAt      types.String `tfsdk:"next_run_at"`
	LastFiredAt    types.String `tfsdk:"last_fired_at"`
	CreatedAt      types.String `tfsdk:"created_at"`
}

func (r *scheduleResource) Metadata(_ context.Context, req resource.MetadataRequest, resp *resource.MetadataResponse) {
	resp.TypeName = req.ProviderTypeName + "_schedule"
}

func (r *scheduleResource) Schema(_ context.Context, _ resource.SchemaRequest, resp *resource.SchemaResponse) {
	resp.Schema = schema.Schema{
		MarkdownDescription: "A PlaceContext schedule (`kind = \"Schedule\"`, with a five-field cron " +
			"expression) or event trigger (`kind = \"Event\"`, with an event name) attached to a job. " +
			"`project_id`, `job_id` and `kind` force replacement.",
		Attributes: map[string]schema.Attribute{
			"id": schema.StringAttribute{
				Computed:            true,
				MarkdownDescription: "Trigger UUID.",
				PlanModifiers:       []planmodifier.String{stringplanmodifier.UseStateForUnknown()},
			},
			"project_id": schema.StringAttribute{
				Required:            true,
				MarkdownDescription: "Project that owns the job the trigger targets.",
				PlanModifiers:       []planmodifier.String{stringplanmodifier.RequiresReplace()},
			},
			"job_id": schema.StringAttribute{
				Required:            true,
				MarkdownDescription: "Job the trigger runs.",
				PlanModifiers:       []planmodifier.String{stringplanmodifier.RequiresReplace()},
			},
			"name": schema.StringAttribute{
				Required:            true,
				MarkdownDescription: "Trigger name.",
			},
			"kind": schema.StringAttribute{
				Required:            true,
				MarkdownDescription: "`Schedule` for a cron trigger or `Event` for an event subscription.",
				Validators:          []validator.String{stringvalidator.OneOf("Schedule", "Event")},
				PlanModifiers:       []planmodifier.String{stringplanmodifier.RequiresReplace()},
			},
			"cron_expression": schema.StringAttribute{
				Optional:            true,
				MarkdownDescription: "Five-field cron expression. Required when `kind = \"Schedule\"`.",
			},
			"event_name": schema.StringAttribute{
				Optional:            true,
				MarkdownDescription: "Subscribed event name. Required when `kind = \"Event\"`.",
			},
			"enabled": schema.BoolAttribute{
				Optional:            true,
				Computed:            true,
				MarkdownDescription: "Whether the trigger is active. Defaults to true.",
			},
			"next_run_at": schema.StringAttribute{
				Computed:            true,
				MarkdownDescription: "Next scheduled run (RFC 3339), or null for event triggers and paused schedules.",
			},
			"last_fired_at": schema.StringAttribute{
				Computed:            true,
				MarkdownDescription: "Last time the trigger fired (RFC 3339), or null.",
			},
			"created_at": schema.StringAttribute{
				Computed:            true,
				MarkdownDescription: "Creation timestamp (RFC 3339).",
				PlanModifiers:       []planmodifier.String{stringplanmodifier.UseStateForUnknown()},
			},
		},
	}
}

func (r *scheduleResource) Configure(_ context.Context, req resource.ConfigureRequest, resp *resource.ConfigureResponse) {
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

func (r *scheduleResource) Create(ctx context.Context, req resource.CreateRequest, resp *resource.CreateResponse) {
	var plan scheduleResourceModel
	resp.Diagnostics.Append(req.Plan.Get(ctx, &plan)...)
	if resp.Diagnostics.HasError() {
		return
	}

	schedule, err := r.client.CreateSchedule(ctx, plan.ProjectID.ValueString(), CreateScheduleRequest{
		JobID:          plan.JobID.ValueString(),
		Name:           plan.Name.ValueString(),
		Kind:           plan.Kind.ValueString(),
		CronExpression: stringPtr(plan.CronExpression),
		EventName:      stringPtr(plan.EventName),
	})
	if err != nil {
		resp.Diagnostics.AddError("Could not create PlaceContext schedule", err.Error())
		return
	}

	// The create endpoint has no enabled flag; pause it afterwards when the
	// configuration asks for a disabled trigger.
	if enabled := boolPtr(plan.Enabled); enabled != nil && !*enabled {
		schedule, err = r.client.UpdateSchedule(ctx, schedule.ID, UpdateScheduleRequest{Enabled: enabled})
		if err != nil {
			resp.Diagnostics.AddError("Could not disable PlaceContext schedule", err.Error())
			return
		}
	}

	resp.Diagnostics.Append(resp.State.Set(ctx, scheduleToModel(schedule))...)
}

func (r *scheduleResource) Read(ctx context.Context, req resource.ReadRequest, resp *resource.ReadResponse) {
	var state scheduleResourceModel
	resp.Diagnostics.Append(req.State.Get(ctx, &state)...)
	if resp.Diagnostics.HasError() {
		return
	}

	schedule, err := r.client.GetSchedule(ctx, state.ID.ValueString())
	if err != nil {
		if IsNotFound(err) {
			resp.State.RemoveResource(ctx)
			return
		}
		resp.Diagnostics.AddError("Could not read PlaceContext schedule", err.Error())
		return
	}
	resp.Diagnostics.Append(resp.State.Set(ctx, scheduleToModel(schedule))...)
}

func (r *scheduleResource) Update(ctx context.Context, req resource.UpdateRequest, resp *resource.UpdateResponse) {
	var plan scheduleResourceModel
	resp.Diagnostics.Append(req.Plan.Get(ctx, &plan)...)
	if resp.Diagnostics.HasError() {
		return
	}

	name := plan.Name.ValueString()
	schedule, err := r.client.UpdateSchedule(ctx, plan.ID.ValueString(), UpdateScheduleRequest{
		Name:           &name,
		CronExpression: stringPtr(plan.CronExpression),
		EventName:      stringPtr(plan.EventName),
		Enabled:        boolPtr(plan.Enabled),
	})
	if err != nil {
		if IsNotFound(err) {
			resp.State.RemoveResource(ctx)
			return
		}
		resp.Diagnostics.AddError("Could not update PlaceContext schedule", err.Error())
		return
	}
	resp.Diagnostics.Append(resp.State.Set(ctx, scheduleToModel(schedule))...)
}

func (r *scheduleResource) Delete(ctx context.Context, req resource.DeleteRequest, resp *resource.DeleteResponse) {
	var state scheduleResourceModel
	resp.Diagnostics.Append(req.State.Get(ctx, &state)...)
	if resp.Diagnostics.HasError() {
		return
	}
	if err := r.client.DeleteSchedule(ctx, state.ID.ValueString()); err != nil && !IsNotFound(err) {
		resp.Diagnostics.AddError("Could not delete PlaceContext schedule", err.Error())
	}
}

func (r *scheduleResource) ImportState(ctx context.Context, req resource.ImportStateRequest, resp *resource.ImportStateResponse) {
	resource.ImportStatePassthroughID(ctx, path.Root("id"), req, resp)
}

func scheduleToModel(schedule *Schedule) *scheduleResourceModel {
	return &scheduleResourceModel{
		ID:             types.StringValue(schedule.ID),
		ProjectID:      types.StringValue(schedule.ProjectID),
		JobID:          types.StringValue(schedule.JobID),
		Name:           types.StringValue(schedule.Name),
		Kind:           types.StringValue(schedule.Kind),
		CronExpression: stringValue(schedule.CronExpression),
		EventName:      stringValue(schedule.EventName),
		Enabled:        types.BoolValue(schedule.Enabled),
		NextRunAt:      stringValue(schedule.NextRunAt),
		LastFiredAt:    stringValue(schedule.LastFiredAt),
		CreatedAt:      types.StringValue(schedule.CreatedAt),
	}
}
