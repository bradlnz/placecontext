package main

import (
	"context"
	"strings"
	"time"

	tea "github.com/charmbracelet/bubbletea"
)

// queryJobs reads the jobs table directly from the Postgres pod via psql.
type searchMsg struct{ body string }

// fetchSearch queries the knowledge graph's contents — decisions, project context, and the change
// ledger — for a term, via psql. Powers the TUI search ([/]).
func (m model) fetchSearch(query string) tea.Cmd {
	mc := m
	q := strings.TrimSpace(query)
	return func() tea.Msg {
		if q == "" {
			return searchMsg{"(type a query and press enter)"}
		}
		ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
		defer cancel()
		esc := strings.ReplaceAll(q, "'", "''")
		// One -c with multiple SELECTs; psql -A -t gives clean unaligned rows. (No backslash meta-commands
		// here — psql -c can't run them, it would swallow the SQL as arguments.)
		sql := `SELECT '- **decision** ' || "Question" || ' → ' || "Choice" FROM decisions ` +
			`WHERE "Question" ILIKE '%` + esc + `%' OR "Choice" ILIKE '%` + esc + `%' OR "Rationale" ILIKE '%` + esc + `%' LIMIT 40;` +
			`SELECT '- **context** ' || left("Markdown", 300) FROM project_contexts WHERE "Markdown" ILIKE '%` + esc + `%' LIMIT 15;` +
			`SELECT '- **activity** ' || "Summary" FROM activity_log ` +
			`WHERE "Summary" ILIKE '%` + esc + `%' OR "Rationale" ILIKE '%` + esc + `%' LIMIT 40;`
		b, err := mc.kubectl(ctx, "-n", ns, "exec", "deploy/placecontext-db", "--",
			"psql", "-U", "postgres", "-d", "placecontext", "-A", "-t", "-c", sql)
		if err != nil {
			return searchMsg{"search failed: " + err.Error()}
		}
		body := strings.TrimSpace(string(b))

		// Also search MinIO files (reports/artifacts), best-effort — skip if MinIO isn't deployed.
		// Excludes the backups bucket (binary). Each hit is listed with how to open it.
		if files := mc.searchMinio(ctx, q); files != "" {
			body += "\n\n### files (minio)\n\n" + files
		}

		if strings.TrimSpace(body) == "" {
			body = "no matches for \"" + q + "\""
		}
		return searchMsg{body}
	}
}

// searchMinio lists object keys (excluding the backups bucket) matching the term, as markdown bullets
// with the command to open each. Returns "" if MinIO isn't present or nothing matches.
func (m model) searchMinio(ctx context.Context, q string) string {
	script := `mc alias set s http://localhost:9000 "$MINIO_ROOT_USER" "$MINIO_ROOT_PASSWORD" >/dev/null 2>&1 || exit 0
mc ls --recursive s 2>/dev/null | awk '{print $NF}' | grep -iv '^placecontext-backups/' | grep -i -- "$Q" | head -25`
	cmd := []string{"-n", ns, "exec", "deploy/minio", "--", "sh", "-c", "Q='" + strings.ReplaceAll(q, "'", "") + "' ; " + script}
	b, err := m.kubectl(ctx, cmd...)
	if err != nil {
		return ""
	}
	var out strings.Builder
	for _, ln := range strings.Split(strings.TrimSpace(string(b)), "\n") {
		ln = strings.TrimSpace(ln)
		if ln == "" {
			continue
		}
		out.WriteString("- `" + ln + "`  — open: `pctl minio open " + ln + "`\n")
	}
	return out.String()
}

func (m model) searchView() string {
	var b strings.Builder
	b.WriteString(titleStyle.Render(" search ") + dimStyle.Render("  decisions · context · activity (the knowledge graph)") + "\n\n")
	b.WriteString("  " + keyStyle.Render("/ ") + m.searchQuery + dimStyle.Render("▌") + "\n\n")
	b.WriteString(boxStyle.Render(m.logs.View()) + "\n")
	return b.String()
}
