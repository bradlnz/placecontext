// PlaceContext test job — Go runtime smoke test.
// Sandbox contract: shard payload JSON on stdin, result JSON on stdout, logs on stderr.
package main

import (
	"encoding/json"
	"fmt"
	"io"
	"os"
)

func main() {
	raw, _ := io.ReadAll(os.Stdin)
	var input any
	if len(raw) > 0 {
		_ = json.Unmarshal(raw, &input)
	}
	fmt.Fprintln(os.Stderr, "hello from go")
	out, _ := json.Marshal(map[string]any{
		"runtime": "go",
		"ok":      true,
		"input":   input,
	})
	_, _ = os.Stdout.Write(out)
}
