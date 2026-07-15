package main

import (
	"encoding/json"
	"fmt"
	"strings"
)

// ── expandable JSON tree for run artifacts ──────────────────────────────────────────────────────
// A run's JSON documents (shard artifacts, emitted .json files, the reduce artifact) open in an
// interactive tree from the run detail ([v]): ↑↓ move, ⏎/space fold/unfold, [e]/[C] expand/collapse
// all, [tab] cycles documents when the run produced more than one.

// jsonDoc is one JSON document surfaced by a run, titled by where it came from.
type jsonDoc struct {
	title string
	raw   string
}

// jsonNode is one row of the tree: a container (object/array) or a leaf value.
type jsonNode struct {
	key      string // object key or "[i]" for array elements; "" at the root
	leaf     string // rendered value for leaves; "" for containers
	kind     byte   // 'o' object, 'a' array, 'v' leaf
	children []*jsonNode
	expanded bool
}

// jsonTree is the viewer state: the run's documents plus cursor/scroll over the active one.
type jsonTree struct {
	docs   []jsonDoc
	doc    int              // active document index
	roots  map[int]*jsonNode // parsed lazily per document
	cursor int
	top    int // first visible row (scroll offset)
}

func newJSONTree(docs []jsonDoc) *jsonTree {
	return &jsonTree{docs: docs, roots: map[int]*jsonNode{}}
}

// root parses (once) and returns the active document's tree.
func (t *jsonTree) root() *jsonNode {
	if r, ok := t.roots[t.doc]; ok {
		return r
	}
	r, err := parseJSONTree(t.docs[t.doc].raw)
	if err != nil {
		r = &jsonNode{kind: 'v', key: "error", leaf: err.Error()}
	}
	t.roots[t.doc] = r
	return r
}

func (t *jsonTree) title() string {
	name := t.docs[t.doc].title
	if len(t.docs) > 1 {
		return fmt.Sprintf("%s (%d/%d)", name, t.doc+1, len(t.docs))
	}
	return name
}

// nextDoc cycles to the run's next JSON document ([tab]).
func (t *jsonTree) nextDoc() {
	t.doc = (t.doc + 1) % len(t.docs)
	t.cursor, t.top = 0, 0
}

// visible flattens the expanded nodes into the rows currently navigable.
type jsonRow struct {
	node  *jsonNode
	depth int
}

func (t *jsonTree) visible() []jsonRow {
	var rows []jsonRow
	var walk func(n *jsonNode, depth int)
	walk = func(n *jsonNode, depth int) {
		rows = append(rows, jsonRow{n, depth})
		if n.expanded {
			for _, c := range n.children {
				walk(c, depth+1)
			}
		}
	}
	walk(t.root(), 0)
	return rows
}

func (t *jsonTree) move(delta int) {
	rows := t.visible()
	t.cursor = min(max(0, t.cursor+delta), len(rows)-1)
}

// toggle folds/unfolds the container under the cursor (a no-op on leaves).
func (t *jsonTree) toggle() {
	rows := t.visible()
	if t.cursor < len(rows) && rows[t.cursor].node.kind != 'v' {
		rows[t.cursor].node.expanded = !rows[t.cursor].node.expanded
	}
}

func (t *jsonTree) setAll(expanded bool) {
	var walk func(n *jsonNode)
	walk = func(n *jsonNode) {
		if n.kind != 'v' {
			n.expanded = expanded
			for _, c := range n.children {
				walk(c)
			}
		}
	}
	walk(t.root())
	if !expanded {
		t.cursor, t.top = 0, 0
	}
}

// render draws the tree windowed to height rows, keeping the cursor in view.
func (t *jsonTree) render(width, height int) string {
	rows := t.visible()
	if t.cursor >= len(rows) {
		t.cursor = max(0, len(rows)-1)
	}
	if t.cursor < t.top {
		t.top = t.cursor
	}
	if t.cursor >= t.top+height {
		t.top = t.cursor - height + 1
	}

	var b strings.Builder
	for i := t.top; i < min(len(rows), t.top+height); i++ {
		line := renderJSONRow(rows[i], i == t.cursor, width)
		b.WriteString(line + "\n")
	}
	if len(rows) > height {
		b.WriteString(dimStyle.Render(fmt.Sprintf(" %d–%d of %d rows", t.top+1, min(len(rows), t.top+height), len(rows))))
	}
	return b.String()
}

func renderJSONRow(r jsonRow, selected bool, width int) string {
	indent := strings.Repeat("  ", r.depth)
	var plain, styled string
	switch r.node.kind {
	case 'v':
		key := r.node.key
		if key != "" {
			key += ": "
		}
		plain = indent + "  " + key + r.node.leaf
		styled = indent + "  " + keyStyle.Render(key) + styleJSONValue(r.node.leaf)
	default:
		marker := "▸"
		if r.node.expanded {
			marker = "▾"
		}
		shape := fmt.Sprintf("{%d}", len(r.node.children))
		if r.node.kind == 'a' {
			shape = fmt.Sprintf("[%d]", len(r.node.children))
		}
		key := r.node.key
		if key == "" {
			key = "(root)"
		}
		plain = indent + marker + " " + key + " " + shape
		styled = indent + marker + " " + titleStyle.Render(key) + " " + dimStyle.Render(shape)
	}
	if len(plain) > width-2 && width > 3 {
		plain = plain[:width-3] + "…"
	}
	if selected {
		// Selected rows use the plain text so the selection background paints the whole line.
		return selStyle.Render("❯" + plain)
	}
	return " " + styled
}

// styleJSONValue colours a rendered leaf by its JSON type.
func styleJSONValue(v string) string {
	switch {
	case strings.HasPrefix(v, `"`):
		return okStyle.Render(v)
	case v == "null" || v == "true" || v == "false":
		return warnStyle.Render(v)
	default:
		return v // numbers stay plain
	}
}

// parseJSONTree decodes raw into a tree, preserving object key order (a map[string]any would
// alphabetise it — the artifact's own field order reads better). Root and first level start open.
func parseJSONTree(raw string) (*jsonNode, error) {
	dec := json.NewDecoder(strings.NewReader(raw))
	dec.UseNumber()
	root, err := decodeJSONNode(dec, "")
	if err != nil {
		return nil, err
	}
	root.expanded = true
	for _, c := range root.children {
		c.expanded = true
	}
	return root, nil
}

func decodeJSONNode(dec *json.Decoder, key string) (*jsonNode, error) {
	tok, err := dec.Token()
	if err != nil {
		return nil, err
	}
	return decodeJSONFrom(dec, key, tok)
}

func decodeJSONFrom(dec *json.Decoder, key string, tok json.Token) (*jsonNode, error) {
	switch v := tok.(type) {
	case json.Delim:
		switch v {
		case '{':
			n := &jsonNode{key: key, kind: 'o'}
			for dec.More() {
				kt, err := dec.Token()
				if err != nil {
					return nil, err
				}
				childKey, _ := kt.(string)
				child, err := decodeJSONNode(dec, childKey)
				if err != nil {
					return nil, err
				}
				n.children = append(n.children, child)
			}
			_, err := dec.Token() // consume '}'
			return n, err
		case '[':
			n := &jsonNode{key: key, kind: 'a'}
			for dec.More() {
				child, err := decodeJSONNode(dec, fmt.Sprintf("[%d]", len(n.children)))
				if err != nil {
					return nil, err
				}
				n.children = append(n.children, child)
			}
			_, err := dec.Token() // consume ']'
			return n, err
		}
		return nil, fmt.Errorf("unexpected delimiter %q", v)
	case string:
		return &jsonNode{key: key, kind: 'v', leaf: fmt.Sprintf("%q", v)}, nil
	case json.Number:
		return &jsonNode{key: key, kind: 'v', leaf: v.String()}, nil
	case bool:
		return &jsonNode{key: key, kind: 'v', leaf: fmt.Sprintf("%v", v)}, nil
	case nil:
		return &jsonNode{key: key, kind: 'v', leaf: "null"}, nil
	}
	return nil, fmt.Errorf("unexpected token %v", tok)
}

// collectRunJSONDocs pulls every JSON document out of a run's persisted shard/reduce results, in
// the order the detail report presents them — these feed the [v] tree viewer.
func collectRunJSONDocs(shardsJSON, reduceJSON string) []jsonDoc {
	var docs []jsonDoc
	add := func(title string, content *string) {
		if content == nil || strings.TrimSpace(*content) == "" {
			return
		}
		if !json.Valid([]byte(*content)) {
			return
		}
		docs = append(docs, jsonDoc{title, *content})
	}

	var shards []shardJson
	if err := json.Unmarshal([]byte(shardsJSON), &shards); err == nil {
		for _, s := range shards {
			add(fmt.Sprintf("shard %d artifact", s.Index), s.Artifact)
			for _, a := range s.Artifacts {
				if !a.IsBinary {
					c := a.Content
					add(fmt.Sprintf("shard %d file %s", s.Index, a.Name), &c)
				}
			}
		}
	}
	if strings.TrimSpace(reduceJSON) != "" {
		var rd reduceJson
		if err := json.Unmarshal([]byte(reduceJSON), &rd); err == nil {
			add("reduce artifact", rd.Artifact)
		}
	}
	return docs
}
