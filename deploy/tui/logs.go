package main

import (
	"context"
	"strings"
	"time"

	tea "github.com/charmbracelet/bubbletea"
	"github.com/charmbracelet/lipgloss"
)

type logsMsg struct{ title, body string }

// fetchLogs returns global logs — the recent tail from every pod in the namespace, prefixed per pod.
func (m model) fetchLogs() tea.Cmd {
	mc := m
	return func() tea.Msg {
		ctx, cancel := context.WithTimeout(context.Background(), 12*time.Second)
		defer cancel()
		names, err := mc.kubectl(ctx, "-n", ns, "get", "pods", "-o", "name")
		if err != nil {
			return logsMsg{"cluster logs", "could not list pods: " + err.Error()}
		}
		var b strings.Builder
		for _, pod := range strings.Fields(string(names)) { // pod/<name>
			out, e := mc.kubectl(ctx, "-n", ns, "logs", pod, "--tail=40", "--prefix", "--all-containers=true")
			if e != nil {
				continue
			}
			b.Write(out)
		}
		body := strings.TrimSpace(b.String())
		if body == "" {
			body = "(no logs)"
		}
		return logsMsg{"cluster logs (all pods)", body}
	}
}

// fetchLogsFor returns logs for a selected pod, or `describe` detail for a node.
func (m model) fetchLogsFor(it selItem) tea.Cmd {
	mc := m
	return func() tea.Msg {
		ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
		defer cancel()
		if it.kind == "pod" {
			title := "pod/" + it.name
			b, err := mc.kubectl(ctx, "-n", ns, "logs", it.name, "--all-containers=true", "--tail=400", "--prefix")
			if err != nil {
				return logsMsg{title, "could not fetch logs: " + err.Error()}
			}
			return logsMsg{title, string(b)}
		}
		// Jobs are handled by the dedicated run-history drill-down (viewRuns/viewRunDetail), not here.
		title := "node/" + it.name
		b, err := mc.kubectl(ctx, "describe", "node", it.name)
		if err != nil {
			return logsMsg{title, "could not describe node: " + err.Error()}
		}
		return logsMsg{title, string(b)}
	}
}

// logLevelStyle picks a colour for a log line by its level (ASP.NET "info:/warn:/fail:/dbug:/trce:"
// prefixes and the usual ERROR/WARN/INFO/DEBUG/TRACE words). Returns ok=false for unclassified lines.
func logLevelStyle(line string) (lipgloss.Style, bool) {
	l := strings.ToLower(line)
	has := func(toks ...string) bool {
		for _, t := range toks {
			if strings.Contains(l, t) {
				return true
			}
		}
		return false
	}
	switch {
	case has("fail:", "fatal", "crit:", "critical", "[error]", " error ", "level=error", "\"error\"", "panic"):
		return errStyle, true
	case has("warn:", "wrn]", "[warn", " warn ", "warning", "level=warn"):
		return warnStyle, true
	case has("dbug:", "trce:", "debug", "trace", "verbose", "level=debug", "level=trace"):
		return dimStyle, true
	case has("info:", "[info", " info ", "level=info"):
		return okStyle, true
	}
	return lipgloss.Style{}, false
}

// colorizeLogs tints each log line by its detected level so the level stands out at a glance.
func colorizeLogs(s string) string {
	lines := strings.Split(s, "\n")
	for i, ln := range lines {
		// Flag ANY line mentioning an error with a marker + red, even when it doesn't match a
		// structured log level (e.g. "DbError", "connection error.", "3 errors"). This makes
		// problems in a pod/node log impossible to miss.
		if strings.Contains(strings.ToLower(ln), "error") {
			lines[i] = errStyle.Render("✗ " + ln)
			continue
		}
		if st, ok := logLevelStyle(ln); ok {
			lines[i] = st.Render(ln)
		}
	}
	return strings.Join(lines, "\n")
}
