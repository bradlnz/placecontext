# Client entrypoint binary — default command is the TUI.
BIN ?= placecontext

.PHONY: build clean install
build:
	CGO_ENABLED=0 go build -trimpath -ldflags "-s -w" -o $(BIN) .

install: build
	install -d "$(HOME)/.local/bin"
	install -m755 $(BIN) "$(HOME)/.local/bin/placecontext"

clean:
	rm -f $(BIN) placecontext-*
