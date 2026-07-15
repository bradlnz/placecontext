package provider

import (
	"context"
	"testing"

	"github.com/hashicorp/terraform-plugin-framework/provider"
	"github.com/hashicorp/terraform-plugin-framework/resource"
)

func TestProviderSchema_NoDiagnostics(t *testing.T) {
	p := New("test")()
	resp := &provider.SchemaResponse{}
	p.Schema(context.Background(), provider.SchemaRequest{}, resp)
	if resp.Diagnostics.HasError() {
		t.Fatalf("provider schema diagnostics: %v", resp.Diagnostics)
	}
	for _, attr := range []string{"endpoint", "api_key"} {
		if _, ok := resp.Schema.Attributes[attr]; !ok {
			t.Errorf("provider schema missing attribute %q", attr)
		}
	}
	if !resp.Schema.Attributes["api_key"].IsSensitive() {
		t.Error("api_key attribute must be sensitive")
	}
}

func TestProviderMetadata(t *testing.T) {
	p := New("1.2.3")()
	resp := &provider.MetadataResponse{}
	p.Metadata(context.Background(), provider.MetadataRequest{}, resp)
	if resp.TypeName != "placecontext" {
		t.Errorf("TypeName = %q, want placecontext", resp.TypeName)
	}
	if resp.Version != "1.2.3" {
		t.Errorf("Version = %q, want 1.2.3", resp.Version)
	}
}

// resourceSchema calls a resource's Schema method and returns the response,
// failing the test if any diagnostics were produced.
func resourceSchema(t *testing.T, r resource.Resource) *resource.SchemaResponse {
	t.Helper()
	resp := &resource.SchemaResponse{}
	r.Schema(context.Background(), resource.SchemaRequest{}, resp)
	if resp.Diagnostics.HasError() {
		t.Fatalf("%T schema diagnostics: %v", r, resp.Diagnostics)
	}
	return resp
}

func TestProjectResourceSchema(t *testing.T) {
	resp := resourceSchema(t, NewProjectResource())
	for _, a := range []string{"id", "path", "name", "status", "is_graphified"} {
		if _, ok := resp.Schema.Attributes[a]; !ok {
			t.Errorf("project schema missing %q", a)
		}
	}
	if !resp.Schema.Attributes["id"].IsComputed() {
		t.Error("project id must be computed")
	}
	if !resp.Schema.Attributes["path"].IsRequired() {
		t.Error("project path must be required")
	}
}

func TestJobResourceSchema(t *testing.T) {
	resp := resourceSchema(t, NewJobResource())
	for _, a := range []string{
		"id", "project_id", "name", "description", "map_image", "map_runtime_id",
		"map_files", "input_payloads", "map_env", "reduce_files", "concurrency_limit",
		"success_exit_codes", "partial_exit_codes", "allow_network_egress", "parameters",
		"post_job_actions", "return_type", "return_file_name", "created_at", "updated_at",
	} {
		if _, ok := resp.Schema.Attributes[a]; !ok {
			t.Errorf("job schema missing %q", a)
		}
	}
	if !resp.Schema.Attributes["project_id"].IsRequired() {
		t.Error("job project_id must be required")
	}
	if !resp.Schema.Attributes["name"].IsRequired() {
		t.Error("job name must be required")
	}
}

func TestScheduleResourceSchema(t *testing.T) {
	resp := resourceSchema(t, NewScheduleResource())
	for _, a := range []string{
		"id", "project_id", "job_id", "name", "kind", "enabled",
		"cron_expression", "event_name", "next_run_at", "last_fired_at", "created_at",
	} {
		if _, ok := resp.Schema.Attributes[a]; !ok {
			t.Errorf("schedule schema missing %q", a)
		}
	}
	if !resp.Schema.Attributes["enabled"].IsComputed() {
		t.Error("schedule enabled should be optional+computed")
	}
}

// TestResourceMetadata verifies the TypeName suffixes.
func TestResourceMetadata(t *testing.T) {
	cases := map[resource.Resource]string{
		NewProjectResource():  "placecontext_project",
		NewJobResource():      "placecontext_job",
		NewScheduleResource(): "placecontext_schedule",
	}
	for r, want := range cases {
		resp := &resource.MetadataResponse{}
		r.Metadata(context.Background(), resource.MetadataRequest{ProviderTypeName: "placecontext"}, resp)
		if resp.TypeName != want {
			t.Errorf("%T TypeName = %q, want %q", r, resp.TypeName, want)
		}
	}
}
