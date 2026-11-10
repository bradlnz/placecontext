package provider

import (
	"errors"
	"strings"

	"github.com/hashicorp/terraform-plugin-framework/diag"
)

// diagError flattens attribute-decoding diagnostics into a Go error so they can
// be surfaced from a shared conversion helper.
func diagError(diags diag.Diagnostics) error {
	var messages []string
	for _, d := range diags.Errors() {
		detail := d.Detail()
		if detail == "" {
			messages = append(messages, d.Summary())
		} else {
			messages = append(messages, d.Summary()+": "+detail)
		}
	}
	if len(messages) == 0 {
		return nil
	}
	return errors.New(strings.Join(messages, "; "))
}
