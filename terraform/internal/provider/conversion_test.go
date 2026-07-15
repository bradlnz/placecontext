package provider

import (
	"context"
	"testing"

	"github.com/bradlnz/terraform-provider-placecontext/internal/client"
	"github.com/hashicorp/terraform-plugin-framework/diag"
	"github.com/hashicorp/terraform-plugin-framework/types"
)

// TestJobToModelToJob verifies the DTO -> model -> DTO round-trip preserves the
// meaningful workload fields, including nested files, lists, maps, and params.
func TestJobToModelToJob(t *testing.T) {
	ctx := context.Background()
	var diags diag.Diagnostics

	label := "Batch"
	desc := "etl"
	in := &client.Job{
		ID:            "job-1",
		ProjectID:     "proj-1",
		Name:          "etl-sync",
		Description:   &desc,
		MapRuntimeID:  strp("python"),
		MapEntrypoint: strp("main.py"),
		MapFiles: []client.FileEntry{
			{Path: "main.py", Content: "print('hi')\n"},
			{Path: "helpers.py", Content: "x=1\n"},
		},
		InputPayloads:      []string{`{"batch":1}`, `{"batch":2}`},
		MapEnv:             map[string]string{"REGION": "us-east-1"},
		ConcurrencyLimit:   4,
		SuccessExitCodes:   []int{0},
		PartialExitCodes:   []int{2},
		AllowNetworkEgress: true,
		Parameters: []client.JobParameter{
			{Name: "batch", Label: &label, Required: true, Type: "string", Options: []string{"a", "b"}},
		},
		PostJobActions: []string{"HtmlReport"},
		ReturnType:     "Table",
	}

	model := jobToModel(ctx, in, &diags)
	if diags.HasError() {
		t.Fatalf("jobToModel diags: %v", diags)
	}
	out := modelToJob(ctx, model, &diags)
	if diags.HasError() {
		t.Fatalf("modelToJob diags: %v", diags)
	}

	if out.Name != in.Name || out.ReturnType != in.ReturnType || out.ConcurrencyLimit != in.ConcurrencyLimit {
		t.Errorf("scalar mismatch: %+v", out)
	}
	if out.AllowNetworkEgress != true {
		t.Error("allow_network_egress lost")
	}
	if len(out.MapFiles) != 2 || out.MapFiles[1].Path != "helpers.py" {
		t.Errorf("map files lost: %+v", out.MapFiles)
	}
	if len(out.InputPayloads) != 2 || out.MapEnv["REGION"] != "us-east-1" {
		t.Errorf("payloads/env lost: %+v / %+v", out.InputPayloads, out.MapEnv)
	}
	if len(out.SuccessExitCodes) != 1 || out.SuccessExitCodes[0] != 0 || len(out.PartialExitCodes) != 1 || out.PartialExitCodes[0] != 2 {
		t.Errorf("exit codes lost: %+v / %+v", out.SuccessExitCodes, out.PartialExitCodes)
	}
	if len(out.Parameters) != 1 || out.Parameters[0].Options[1] != "b" || out.Parameters[0].Label == nil || *out.Parameters[0].Label != "Batch" {
		t.Errorf("parameters lost: %+v", out.Parameters)
	}
}

// TestEmptyCollectionsBecomeNull ensures omitted optional collections round-trip
// as null (not empty), so an image-based job with no reduce step does not drift.
func TestEmptyCollectionsBecomeNull(t *testing.T) {
	ctx := context.Background()
	var diags diag.Diagnostics

	in := &client.Job{
		ID:        "job-2",
		ProjectID: "proj-1",
		Name:      "nightly",
		MapImage:  strp("ghcr.io/acme/worker:latest"),
		MapFiles:  []client.FileEntry{}, // server returns [] for an image job
		ReturnType: "Json",
	}
	m := jobToModel(ctx, in, &diags)
	if diags.HasError() {
		t.Fatalf("diags: %v", diags)
	}
	if !m.MapFiles.IsNull() {
		t.Error("empty map_files should map to null")
	}
	if !m.InputPayloads.IsNull() {
		t.Error("nil input_payloads should map to null")
	}
	if !m.ReduceImage.IsNull() {
		t.Error("nil reduce_image should be null")
	}
	if m.MapImage.ValueString() != "ghcr.io/acme/worker:latest" {
		t.Errorf("map_image = %q", m.MapImage.ValueString())
	}
}

func TestScheduleToModel(t *testing.T) {
	cron := "0 2 * * *"
	next := "2026-07-16T02:00:00+00:00"
	m := scheduleToModel(&client.Schedule{
		ID: "s1", ProjectID: "p1", JobID: "j1", Name: "nightly",
		Kind: "Schedule", Enabled: true, CronExpression: &cron, NextRunAt: &next,
		CreatedAt: "2026-07-15T12:00:00+00:00",
	})
	if m.CronExpression.ValueString() != cron || !m.EventName.IsNull() {
		t.Errorf("cron/event mapping wrong: %+v", m)
	}
	if !m.Enabled.ValueBool() || m.NextRunAt.ValueString() != next {
		t.Errorf("enabled/next mapping wrong: %+v", m)
	}
}

func TestProjectToModel_EmptyStatusNull(t *testing.T) {
	m := projectToModel(&client.Project{ID: "p1", Name: "x", Path: "/x"})
	if !m.Status.IsNull() {
		t.Error("empty status should be null")
	}
	if types.StringValue("p1") != m.ID {
		t.Error("id mismatch")
	}
}

func strp(s string) *string { return &s }
