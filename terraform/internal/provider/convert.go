package provider

import (
	"context"

	"github.com/hashicorp/terraform-plugin-framework/attr"
	"github.com/hashicorp/terraform-plugin-framework/types"
)

// ── Terraform → Go ──────────────────────────────────────────────────────────

func stringPtr(value types.String) *string {
	if value.IsNull() || value.IsUnknown() {
		return nil
	}
	v := value.ValueString()
	return &v
}

func boolPtr(value types.Bool) *bool {
	if value.IsNull() || value.IsUnknown() {
		return nil
	}
	v := value.ValueBool()
	return &v
}

func intPtr(value types.Int64) *int {
	if value.IsNull() || value.IsUnknown() {
		return nil
	}
	v := int(value.ValueInt64())
	return &v
}

func stringsFromList(ctx context.Context, value types.List) ([]string, error) {
	if value.IsNull() || value.IsUnknown() {
		return nil, nil
	}
	out := make([]string, 0, len(value.Elements()))
	if diags := value.ElementsAs(ctx, &out, false); diags.HasError() {
		return nil, diagError(diags)
	}
	return out, nil
}

func intsFromList(ctx context.Context, value types.List) ([]int, error) {
	if value.IsNull() || value.IsUnknown() {
		return nil, nil
	}
	var values []int64
	if diags := value.ElementsAs(ctx, &values, false); diags.HasError() {
		return nil, diagError(diags)
	}
	out := make([]int, len(values))
	for i, v := range values {
		out[i] = int(v)
	}
	return out, nil
}

func stringsFromMap(ctx context.Context, value types.Map) (map[string]string, error) {
	if value.IsNull() || value.IsUnknown() {
		return nil, nil
	}
	out := make(map[string]string, len(value.Elements()))
	if diags := value.ElementsAs(ctx, &out, false); diags.HasError() {
		return nil, diagError(diags)
	}
	return out, nil
}

// ── Go → Terraform ──────────────────────────────────────────────────────────

func stringValue(value *string) types.String {
	if value == nil {
		return types.StringNull()
	}
	return types.StringValue(*value)
}

func stringsToList(values []string) types.List {
	if values == nil {
		return types.ListNull(types.StringType)
	}
	elems := make([]attr.Value, len(values))
	for i, v := range values {
		elems[i] = types.StringValue(v)
	}
	return types.ListValueMust(types.StringType, elems)
}

func intsToList(values []int) types.List {
	if values == nil {
		return types.ListNull(types.Int64Type)
	}
	elems := make([]attr.Value, len(values))
	for i, v := range values {
		elems[i] = types.Int64Value(int64(v))
	}
	return types.ListValueMust(types.Int64Type, elems)
}

func stringsToMap(values map[string]string) types.Map {
	if values == nil {
		return types.MapNull(types.StringType)
	}
	elems := make(map[string]attr.Value, len(values))
	for k, v := range values {
		elems[k] = types.StringValue(v)
	}
	return types.MapValueMust(types.StringType, elems)
}
