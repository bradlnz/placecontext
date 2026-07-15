package provider

import (
	"context"
	"errors"

	"github.com/bradlnz/terraform-provider-placecontext/internal/client"
	"github.com/hashicorp/terraform-plugin-framework/path"
	"github.com/hashicorp/terraform-plugin-framework/resource"
	"github.com/hashicorp/terraform-plugin-framework/resource/schema"
	"github.com/hashicorp/terraform-plugin-framework/resource/schema/booldefault"
	"github.com/hashicorp/terraform-plugin-framework/resource/schema/planmodifier"
	"github.com/hashicorp/terraform-plugin-framework/resource/schema/stringplanmodifier"
	"github.com/hashicorp/terraform-plugin-framework/types"
)

var (
	_ resource.Resource                = (*scheduleResource)(nil)
	_ resource.ResourceWithConfigure   = (*scheduleResource)(nil)
	_ resource.ResourceWithImportState = (*scheduleResource)(nil)
)

// NewScheduleResource constructs the placecontext_schedule resource.
func NewScheduleResource() resource.Resource { return &scheduleResource{} }

type scheduleResource struct {
	client *client.Client
}

type scheduleModel struct {
	ID             types.String `tfsdk:"id"`
	ProjectID      types.String `tfsdk:"project_id"`
	JobID          types.String `tfsdk:"job_id"`
	Name           types.String `tfsdk:"name"`
	Kind           types.String `tfsdk:"kind"`
	Enabled        types.Bool   `tfsdk:"enabled"`
	CronExpression types.String `tfsdk:"cron_expression"`
	EventName      types.String `tfsdk:"event_name"`
	NextRunAt      types.String `tfsdk:"next_run_at"`
	LastFiredAt    types.String `tfsdk:"last_fired_at"`
	CreatedAt      types.String `tfsdk:"created_at"`
}

func (r *scheduleResource) Metadata(_ context.Context, req resource.MetadataRequest, resp *resource.MetadataResponse) {
	resp.TypeName = req.ProviderTypeName + "_schedule"
}

func (r *scheduleResource) Schema(_ context.Context, _ resource.SchemaRequest, resp *resource.SchemaResponse) {
	// Every field except `enabled` forces replacement: the API's only supported
	// mutation on an existing schedule is enable/disable, so changing the cron,
	// event, name, job, or kind must delete-and-recreate.
	forceReplace := []planmodifier.String{stringplanmodifier.RequiresReplace()}
	useState := []planmodifier.String{stringplanmodifier.UseStateForUnknown()}

	resp.Schema = schema.Schema{
		Description: "A trigger on a PlaceContext job: a cron schedule (kind = \"Schedule\") or an event " +
			"subscription (kind = \"Event\"). Only `enabled` is mutable in place; changing any other " +
			"argument forces replacement.",
		Attributes: map[string]schema.Attribute{
			"id": schema.StringAttribute{
				Computed:      true,
				Description:   "Server-assigned schedule id (GUID).",
				PlanModifiers: useState,
			},
			"project_id": schema.StringAttribute{
				Required:      true,
				Description:   "Id of the project the schedule (and its job) belongs to.",
				PlanModifiers: forceReplace,
			},
			"job_id": schema.StringAttribute{
				Required:      true,
				Description:   "Id of the job this trigger fires.",
				PlanModifiers: forceReplace,
			},
			"name": schema.StringAttribute{
				Required:      true,
				Description:   "Human-readable trigger name.",
				PlanModifiers: forceReplace,
			},
			"kind": schema.StringAttribute{
				Required:      true,
				Description:   "\"Schedule\" (cron) or \"Event\" (event subscription).",
				PlanModifiers: forceReplace,
			},
			"cron_expression": schema.StringAttribute{
				Optional:      true,
				Description:   "Cron expression for a \"Schedule\" kind, e.g. \"0 2 * * *\".",
				PlanModifiers: forceReplace,
			},
			"event_name": schema.StringAttribute{
				Optional:      true,
				Description:   "Event name for an \"Event\" kind, e.g. \"deploy.finished\".",
				PlanModifiers: forceReplace,
			},
			"enabled": schema.BoolAttribute{
				Optional:    true,
				Computed:    true,
				Default:     booldefault.StaticBool(true),
				Description: "Whether the trigger is active. This is the only in-place mutation the API supports.",
			},
			"next_run_at": schema.StringAttribute{
				Computed:      true,
				Description:   "Computed next fire time for a cron schedule.",
				PlanModifiers: useState,
			},
			"last_fired_at": schema.StringAttribute{
				Computed:      true,
				Description:   "When the trigger last fired.",
				PlanModifiers: useState,
			},
			"created_at": schema.StringAttribute{
				Computed:      true,
				Description:   "Creation timestamp (ISO-8601).",
				PlanModifiers: useState,
			},
		},
	}
}

func (r *scheduleResource) Configure(_ context.Context, req resource.ConfigureRequest, resp *resource.ConfigureResponse) {
	r.client = clientFromProviderData(req.ProviderData, &resp.Diagnostics)
}

func (r *scheduleResource) Create(ctx context.Context, req resource.CreateRequest, resp *resource.CreateResponse) {
	var plan scheduleModel
	resp.Diagnostics.Append(req.Plan.Get(ctx, &plan)...)
	if resp.Diagnostics.HasError() {
		return
	}

	created, err := r.client.CreateSchedule(ctx, plan.ProjectID.ValueString(), client.CreateScheduleRequest{
		JobID:          plan.JobID.ValueString(),
		Name:           plan.Name.ValueString(),
		Kind:           plan.Kind.ValueString(),
		CronExpression: strPtr(plan.CronExpression),
		EventName:      strPtr(plan.EventName),
	})
	if err != nil {
		resp.Diagnostics.AddError("Error creating schedule", err.Error())
		return
	}

	// Schedules are created enabled. If the plan asked for disabled, apply it.
	if !plan.Enabled.IsNull() && !plan.Enabled.ValueBool() {
		created, err = r.client.UpdateSchedule(ctx, created.ID, false)
		if err != nil {
			resp.Diagnostics.AddError("Error disabling newly created schedule", err.Error())
			return
		}
	}

	resp.Diagnostics.Append(resp.State.Set(ctx, scheduleToModel(created))...)
}

func (r *scheduleResource) Read(ctx context.Context, req resource.ReadRequest, resp *resource.ReadResponse) {
	var state scheduleModel
	resp.Diagnostics.Append(req.State.Get(ctx, &state)...)
	if resp.Diagnostics.HasError() {
		return
	}

	s, err := r.client.GetSchedule(ctx, state.ID.ValueString())
	if err != nil {
		if errors.Is(err, client.ErrNotFound) {
			resp.State.RemoveResource(ctx)
			return
		}
		resp.Diagnostics.AddError("Error reading schedule", err.Error())
		return
	}

	resp.Diagnostics.Append(resp.State.Set(ctx, scheduleToModel(s))...)
}

// Update only ever changes `enabled` — all other fields are RequiresReplace.
func (r *scheduleResource) Update(ctx context.Context, req resource.UpdateRequest, resp *resource.UpdateResponse) {
	var plan scheduleModel
	resp.Diagnostics.Append(req.Plan.Get(ctx, &plan)...)
	if resp.Diagnostics.HasError() {
		return
	}

	updated, err := r.client.UpdateSchedule(ctx, plan.ID.ValueString(), plan.Enabled.ValueBool())
	if err != nil {
		resp.Diagnostics.AddError("Error updating schedule", err.Error())
		return
	}

	resp.Diagnostics.Append(resp.State.Set(ctx, scheduleToModel(updated))...)
}

func (r *scheduleResource) Delete(ctx context.Context, req resource.DeleteRequest, resp *resource.DeleteResponse) {
	var state scheduleModel
	resp.Diagnostics.Append(req.State.Get(ctx, &state)...)
	if resp.Diagnostics.HasError() {
		return
	}

	err := r.client.DeleteSchedule(ctx, state.ID.ValueString())
	if err != nil && !errors.Is(err, client.ErrNotFound) {
		resp.Diagnostics.AddError("Error deleting schedule", err.Error())
	}
}

func (r *scheduleResource) ImportState(ctx context.Context, req resource.ImportStateRequest, resp *resource.ImportStateResponse) {
	resource.ImportStatePassthroughID(ctx, path.Root("id"), req, resp)
}

func scheduleToModel(s *client.Schedule) scheduleModel {
	return scheduleModel{
		ID:             types.StringValue(s.ID),
		ProjectID:      types.StringValue(s.ProjectID),
		JobID:          types.StringValue(s.JobID),
		Name:           strValueOrNull(s.Name),
		Kind:           strValueOrNull(s.Kind),
		Enabled:        types.BoolValue(s.Enabled),
		CronExpression: strFromPtr(s.CronExpression),
		EventName:      strFromPtr(s.EventName),
		NextRunAt:      strFromPtr(s.NextRunAt),
		LastFiredAt:    strFromPtr(s.LastFiredAt),
		CreatedAt:      strValueOrNull(s.CreatedAt),
	}
}
