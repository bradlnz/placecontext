// terraform-provider-placecontext is a Terraform provider that manages
// PlaceContext projects, jobs, and schedules declaratively through the
// workspace management REST API (/api/v1).
package main

import (
	"context"
	"flag"
	"log"

	"github.com/bradlnz/terraform-provider-placecontext/internal/provider"
	"github.com/hashicorp/terraform-plugin-framework/providerserver"
)

// version is set at build time via -ldflags "-X main.version=...".
var version = "dev"

func main() {
	var debug bool
	flag.BoolVar(&debug, "debug", false, "set to run the provider with support for debuggers like delve")
	flag.Parse()

	opts := providerserver.ServeOpts{
		// Matches the provider source address used in required_providers /
		// dev_overrides, e.g. bradlnz/placecontext.
		Address: "registry.terraform.io/bradlnz/placecontext",
		Debug:   debug,
	}

	if err := providerserver.Serve(context.Background(), provider.New(version), opts); err != nil {
		log.Fatal(err.Error())
	}
}
