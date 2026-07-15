package main

import (
	"strings"
	"testing"
)

func TestParseJSONTreePreservesOrderAndTypes(t *testing.T) {
	root, err := parseJSONTree(`{"zeta":1,"alpha":"x","nested":{"b":true,"a":null},"list":[10,"s",false]}`)
	if err != nil {
		t.Fatal(err)
	}
	keys := []string{}
	for _, c := range root.children {
		keys = append(keys, c.key)
	}
	if got := strings.Join(keys, ","); got != "zeta,alpha,nested,list" {
		t.Fatalf("object key order not preserved: %s", got)
	}
	list := root.children[3]
	if list.kind != 'a' || len(list.children) != 3 || list.children[1].leaf != `"s"` {
		t.Fatalf("array parse wrong: %+v", list)
	}
	if root.children[2].children[1].leaf != "null" {
		t.Fatalf("null leaf wrong: %+v", root.children[2].children[1])
	}
}

func TestTreeToggleAndVisible(t *testing.T) {
	tr := newJSONTree([]jsonDoc{{"doc", `{"a":{"b":{"c":1}},"d":2}`}})
	// Root and first level open by default: root, a, d, and a's child b (collapsed) = 4 rows.
	if got := len(tr.visible()); got != 4 {
		t.Fatalf("default visible rows = %d, want 4", got)
	}
	// Cursor to "b" (row 2: root=0, a=1, b=2) and expand it -> c appears.
	tr.cursor = 2
	tr.toggle()
	if got := len(tr.visible()); got != 5 {
		t.Fatalf("after expanding b: rows = %d, want 5", got)
	}
	tr.setAll(false)
	if got := len(tr.visible()); got != 1 {
		t.Fatalf("after collapse all: rows = %d, want 1 (root)", got)
	}
	tr.setAll(true)
	if got := len(tr.visible()); got != 5 {
		t.Fatalf("after expand all: rows = %d, want 5", got)
	}
}

func TestCollectRunJSONDocs(t *testing.T) {
	shards := `[{"index":0,"exitCode":0,"outcome":"Succeeded","artifact":"{\"ok\":true}","log":null,
	  "artifacts":[{"name":"data.json","content":"[1,2]"},{"name":"doc.pdf","content":"JVBERg==","isBinary":true},{"name":"notes.txt","content":"not json"}]}]`
	reduce := `{"exitCode":0,"succeeded":true,"artifact":"{\"sum\":3}","log":null,"artifacts":[]}`
	docs := collectRunJSONDocs(shards, reduce)
	if len(docs) != 3 {
		t.Fatalf("docs = %d, want 3 (shard artifact, data.json, reduce)", len(docs))
	}
	if docs[0].title != "shard 0 artifact" || docs[1].title != "shard 0 file data.json" || docs[2].title != "reduce artifact" {
		t.Fatalf("unexpected titles: %q %q %q", docs[0].title, docs[1].title, docs[2].title)
	}
}

func TestRenderWindowsAndClampsCursor(t *testing.T) {
	tr := newJSONTree([]jsonDoc{{"doc", `[1,2,3,4,5,6,7,8,9,10]`}})
	tr.setAll(true)
	tr.cursor = 10 // last row
	out := tr.render(60, 4)
	if !strings.Contains(out, "of 11 rows") {
		t.Fatalf("expected windowed footer, got:\n%s", out)
	}
	if !strings.Contains(out, "10") {
		t.Fatalf("cursor row not in window:\n%s", out)
	}
}
