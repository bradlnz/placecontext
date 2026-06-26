# pctl-tui — build the reactive PlaceContext TUI.
#
#   make            build a static binary for this host (./pctl-tui)
#   make install    build + copy to ~/.local/bin
#   make fleet      cross-compile static binaries for common fleet targets (./dist)
#   make clean      remove build artifacts
BINARY := pctl-tui
# Stamp the newest EF migration id from the source tree so the TUI can warn when the live DB schema
# is behind the app code (pending migrations). Excludes *.Designer.cs and the model snapshot.
MIGRATIONS_DIR := ../../src/PlaceContext.Infrastructure/Migrations
EXPECTED_MIGRATION := $(shell ls $(MIGRATIONS_DIR)/[0-9]*_*.cs 2>/dev/null | grep -vi '\.designer\.cs$$' | sed 's#.*/##; s#\.cs$$##' | sort | tail -1)
LDFLAGS := -s -w -X main.expectedMigration=$(EXPECTED_MIGRATION)
GOFLAGS := -trimpath

.PHONY: build install fleet clean
build:
	CGO_ENABLED=0 go build $(GOFLAGS) -ldflags "$(LDFLAGS)" -o $(BINARY) .

install: build
	install -Dm755 $(BINARY) $(HOME)/.local/bin/$(BINARY)
	@echo "installed → $(HOME)/.local/bin/$(BINARY)"

fleet:
	@mkdir -p dist
	CGO_ENABLED=0 GOOS=linux  GOARCH=amd64 go build $(GOFLAGS) -ldflags "$(LDFLAGS)" -o dist/$(BINARY)-linux-amd64 .
	CGO_ENABLED=0 GOOS=linux  GOARCH=arm64 go build $(GOFLAGS) -ldflags "$(LDFLAGS)" -o dist/$(BINARY)-linux-arm64 .
	CGO_ENABLED=0 GOOS=darwin GOARCH=arm64 go build $(GOFLAGS) -ldflags "$(LDFLAGS)" -o dist/$(BINARY)-darwin-arm64 .
	@echo "built → dist/"

clean:
	rm -f $(BINARY)
	rm -rf dist
