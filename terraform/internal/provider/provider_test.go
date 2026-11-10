package provider

import (
	"context"
	"testing"

	"github.com/hashicorp/terraform-plugin-framework/datasource"
	fwprovider "github.com/hashicorp/terraform-plugin-framework/provider"
	fwresource "github.com/hashicorp/terraform-plugin-framework/resource"
)

func TestProviderMetadata(t *testing.T) {
	p := New("test-version")()
	var meta fwprovider.MetadataResponse
	p.Metadata(context.Background(), fwprovider.MetadataRequest{}, &meta)

	if meta.TypeName != "placecontext" {
		t.Errorf("TypeName = %q, want placecontext", meta.TypeName)
	}
	if meta.Version != "test-version" {
		t.Errorf("Version = %q, want test-version", meta.Version)
	}
}

func TestProviderSchema(t *testing.T) {
	p := New("test")()
	var resp fwprovider.SchemaResponse
	p.Schema(context.Background(), fwprovider.SchemaRequest{}, &resp)

	baseURL, ok := resp.Schema.Attributes["base_url"]
	if !ok || baseURL == nil {
		t.Fatal("provider schema is missing base_url")
	}
	if !baseURL.IsOptional() {
		t.Error("base_url should be optional so PLACECONTEXT_URL can supply it")
	}
	apiKey, ok := resp.Schema.Attributes["api_key"]
	if !ok || apiKey == nil {
		t.Fatal("provider schema is missing api_key")
	}
	if !apiKey.IsSensitive() {
		t.Error("api_key must be marked sensitive")
	}
}

func TestResourcesAreRegistered(t *testing.T) {
	p := New("test")()
	ctx := context.Background()

	got := map[string]bool{}
	for _, factory := range p.Resources(ctx) {
		var meta fwresource.MetadataResponse
		factory().Metadata(ctx, fwresource.MetadataRequest{ProviderTypeName: "placecontext"}, &meta)
		got[meta.TypeName] = true
	}
	for _, want := range []string{"placecontext_project", "placecontext_job", "placecontext_schedule"} {
		if !got[want] {
			t.Errorf("resource %q is not registered", want)
		}
	}
}

func TestDataSourcesAreRegistered(t *testing.T) {
	p := New("test")()
	ctx := context.Background()

	got := map[string]bool{}
	for _, factory := range p.DataSources(ctx) {
		var meta datasource.MetadataResponse
		factory().Metadata(ctx, datasource.MetadataRequest{ProviderTypeName: "placecontext"}, &meta)
		got[meta.TypeName] = true
	}
	for _, want := range []string{"placecontext_project", "placecontext_job"} {
		if !got[want] {
			t.Errorf("data source %q is not registered", want)
		}
	}
}

func TestResourceSchemasBuild(t *testing.T) {
	ctx := context.Background()
	factories := map[string]func() fwresource.Resource{
		"project":  newProjectResource,
		"job":      newJobResource,
		"schedule": newScheduleResource,
	}
	for name, factory := range factories {
		var resp fwresource.SchemaResponse
		factory().Schema(ctx, fwresource.SchemaRequest{}, &resp)
		if len(resp.Schema.Attributes) == 0 {
			t.Errorf("%s resource schema is empty", name)
		}
		if resp.Diagnostics.HasError() {
			t.Errorf("%s resource schema reported errors: %v", name, resp.Diagnostics)
		}
	}
}

func TestDataSourceSchemasBuild(t *testing.T) {
	ctx := context.Background()
	factories := map[string]func() datasource.DataSource{
		"project": newProjectDataSource,
		"job":     newJobDataSource,
	}
	for name, factory := range factories {
		var resp datasource.SchemaResponse
		factory().Schema(ctx, datasource.SchemaRequest{}, &resp)
		if len(resp.Schema.Attributes) == 0 {
			t.Errorf("%s data source schema is empty", name)
		}
		if resp.Diagnostics.HasError() {
			t.Errorf("%s data source schema reported errors: %v", name, resp.Diagnostics)
		}
	}
}
