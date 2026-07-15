# Client operator TUI (install / upgrade / connect). Not the old ops dashboard.
BIN ?= placecontext-tui

.PHONY: build clean
build:
	CGO_ENABLED=0 go build -trimpath -ldflags "-s -w" -o $(BIN) .

clean:
	rm -f $(BIN) placecontext-tui-*
