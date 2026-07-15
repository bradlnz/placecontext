package main

import (
	"context"
	"fmt"
	"strings"
	"time"

	tea "github.com/charmbracelet/bubbletea"
)

type mcpRow struct{ id, at, tool, dir, status, dur string }
type mcpMsg struct {
	rows []mcpRow
	err  string
}

// fetchMcp reads recent MCP/tool calls from the tool_calls table via psql.
func (m model) fetchMcp() tea.Cmd {
	mc := m
	return func() tea.Msg {
		ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
		defer cancel()
		const q = `SELECT "Id", "At", "Tool", "Direction", "Status", "DurationMs" ` +
			`FROM tool_calls ORDER BY "At" DESC LIMIT 200`
		b, err := mc.kubectl(ctx, "-n", ns, "exec", "deploy/placecontext-db", "--",
			"psql", "-U", "postgres", "-d", "placecontext", "-At", "-F", "\t", "-c", q)
		if err != nil {
			return mcpMsg{nil, "could not query tool_calls: " + err.Error()}
		}
		var rows []mcpRow
		for _, ln := range strings.Split(strings.TrimSpace(string(b)), "\n") {
			if ln == "" {
				continue
			}
			f := strings.Split(ln, "\t")
			for len(f) < 6 {
				f = append(f, "")
			}
			at := f[1]
			if len(at) > 19 {
				at = at[:19]
			}
			rows = append(rows, mcpRow{f[0], at, f[2], f[3], f[4], f[5]})
		}
		return mcpMsg{rows, ""}
	}
}

// fetchMcpDetail loads one tool call's request/response payloads (shown like pod logs).
func (m model) fetchMcpDetail(c mcpRow) tea.Cmd {
	mc := m
	return func() tea.Msg {
		ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
		defer cancel()
		esc := strings.ReplaceAll(c.id, "'", "''")
		q := `SELECT 'REQUEST', "RequestJson", 'RESPONSE', "ResponseJson" FROM tool_calls WHERE "Id"='` + esc + `'`
		b, err := mc.kubectl(ctx, "-n", ns, "exec", "deploy/placecontext-db", "--",
			"psql", "-U", "postgres", "-d", "placecontext", "-A", "-F", "\n", "-t", "-c", q)
		title := "mcp/" + c.tool + " · " + c.at
		if err != nil {
			return logsMsg{title, "could not fetch call detail: " + err.Error()}
		}
		body := strings.TrimSpace(string(b))
		if body == "" {
			body = "(no payload recorded)"
		}
		return logsMsg{title, body}
	}
}

func (m model) mcpView() string {
	var b strings.Builder
	b.WriteString(titleStyle.Render(fmt.Sprintf(" MCP / tool calls (%d) ", len(m.mcp))) + "\n\n")
	if m.mcpErr != "" {
		b.WriteString(errStyle.Render("  "+m.mcpErr) + "\n")
		return b.String()
	}
	b.WriteString("  " + headStyle.Render(pad("TIME", 21)+pad("TOOL", 26)+pad("DIR", 6)+pad("STATUS", 10)+"ms") + dimStyle.Render("   (⏎ view request/response)") + "\n")
	if len(m.mcp) == 0 {
		b.WriteString(dimStyle.Render("    (no MCP calls recorded yet)") + "\n")
		return b.String()
	}
	for i, c := range m.mcp {
		plain := pad(c.at, 21) + pad(trunc(c.tool, 25), 26) + pad(c.dir, 6) + pad(c.status, 10) + c.dur
		if i == m.mcpCursor {
			b.WriteString(selStyle.Render("❯ "+plain) + "\n")
			continue
		}
		st := okStyle.Render(pad(c.status, 10))
		if c.status != "" && c.status != "ok" && c.status != "success" && c.status != "Success" {
			st = warnStyle.Render(pad(c.status, 10))
		}
		b.WriteString("  " + dimStyle.Render(pad(c.at, 21)) + pad(trunc(c.tool, 25), 26) + pad(c.dir, 6) + st + dimStyle.Render(c.dur) + "\n")
	}
	return b.String()
}
