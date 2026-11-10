// Command terraform-provider-placecontext exposes the PlaceContext management API
// (/api/v1) as Terraform resources for projects, jobs and schedules.
package main

import (
	"context"
	"flag"
	"log"

	"github.com/bradlnz/placecontext/terraform/internal/provider"
	"github.com/hashicorp/terraform-plugin-framework/providerserver"
)

// version is overridden at build time with -ldflags "-X main.version=<tag>".
var version = "dev"

func main() {
	var debug bool
	flag.BoolVar(&debug, "debug", false,
		"run the provider with debugger support (see https://developer.hashicorp.com/terraform/plugin/debugging)")
	flag.Parse()

	err := providerserver.Serve(context.Background(), provider.New(version), providerserver.ServeOpts{
		Address: "registry.terraform.io/bradlnz/placecontext",
		Debug:   debug,
	})
	if err != nil {
		log.Fatal(err)
	}
}
