package provider

import (
	"github.com/bradlnz/terraform-provider-placecontext/internal/client"
	"github.com/hashicorp/terraform-plugin-framework/diag"
	"github.com/hashicorp/terraform-plugin-framework/path"
	"github.com/hashicorp/terraform-plugin-framework/types"
)

func pathRoot(name string) path.Path { return path.Root(name) }

// clientFromProviderData extracts the shared *client.Client passed by the
// provider's Configure. It tolerates a nil ProviderData (which happens during
// early framework lifecycle phases before Configure has run).
func clientFromProviderData(providerData any, diags *diag.Diagnostics) *client.Client {
	if providerData == nil {
		return nil
	}
	c, ok := providerData.(*client.Client)
	if !ok {
		diags.AddError(
			"Unexpected provider data",
			"Expected *client.Client from the provider. This is a bug in the provider.",
		)
		return nil
	}
	return c
}

// strPtr bridges a nullable Terraform string to a *string DTO field.
func strPtr(v types.String) *string {
	if v.IsNull() || v.IsUnknown() {
		return nil
	}
	s := v.ValueString()
	return &s
}

// strFromPtr bridges a *string DTO field to a nullable Terraform string.
func strFromPtr(p *string) types.String {
	if p == nil {
		return types.StringNull()
	}
	return types.StringValue(*p)
}

// strValueOrNull returns a null string for "" so an omitted optional attribute
// stays null in state instead of becoming an empty string.
func strValueOrNull(s string) types.String {
	if s == "" {
		return types.StringNull()
	}
	return types.StringValue(s)
}
