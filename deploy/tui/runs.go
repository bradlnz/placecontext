package main

import (
	"context"
	"encoding/json"
	"fmt"
	"os/exec"
	"regexp"
	"strings"
	"time"

	tea "github.com/charmbracelet/bubbletea"
)

// ── run-history drill-down ──────────────────────────────────────────────────────────────────────
// runRow is one row in the runs list for a job.
type runRow struct{ id, status, started, finished, duration string }

type runsMsg struct {
	rows []runRow
	err  string
}
type runDetailMsg struct{ title, body string }

// fetchRuns reads the recent runs for a job (newest first) from job_runs via psql.
func (m model) fetchRuns(j jobRow) tea.Cmd {
	mc := m
	return func() tea.Msg {
		if !validUUID(j.id) {
			return runsMsg{nil, "invalid job id (refresh and retry)"}
		}
		ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
		defer cancel()
		// EXTRACT(EPOCH …) gives a numeric duration we format client-side; NULL finished ⇒ still running.
		q := `SELECT "Id","Status",` +
			`to_char("StartedAt",'YYYY-MM-DD HH24:MI:SS'),` +
			`coalesce(to_char("FinishedAt",'YYYY-MM-DD HH24:MI:SS'),''),` +
			`coalesce(round(EXTRACT(EPOCH FROM ("FinishedAt"-"StartedAt")))::text,'') ` +
			`FROM job_runs WHERE "JobId"='` + j.id + `' ORDER BY "StartedAt" DESC LIMIT 50`
		b, err := mc.kubectl(ctx, "-n", ns, "exec", "deploy/placecontext-db", "--",
			"psql", "-U", "postgres", "-d", "placecontext", "-At", "-F", "\t", "-c", q)
		if err != nil {
			return runsMsg{nil, "could not query runs: " + err.Error()}
		}
		var rows []runRow
		for _, ln := range strings.Split(strings.TrimSpace(string(b)), "\n") {
			if ln == "" {
				continue
			}
			f := strings.Split(ln, "\t")
			for len(f) < 5 {
				f = append(f, "")
			}
			dur := f[4]
			if dur != "" {
				dur += "s"
			} else if f[3] == "" {
				dur = "running…"
			}
			rows = append(rows, runRow{f[0], f[1], f[2], f[3], dur})
		}
		return runsMsg{rows, ""}
	}
}

// shardJson / reduceJson / artifactJson mirror the camelCase JSON persisted by EfJobRunRepository.
type artifactJson struct {
	Name    string `json:"name"`
	Content string `json:"content"`
}
type shardJson struct {
	Index     int            `json:"index"`
	ExitCode  int            `json:"exitCode"`
	Outcome   string         `json:"outcome"`
	Artifact  *string        `json:"artifact"`
	Log       *string        `json:"log"`
	Artifacts []artifactJson `json:"artifacts"`
}
type reduceJson struct {
	ExitCode  int            `json:"exitCode"`
	Succeeded bool           `json:"succeeded"`
	Artifact  *string        `json:"artifact"`
	Log       *string        `json:"log"`
	Artifacts []artifactJson `json:"artifacts"`
}

// fetchRunDetail reads one run's shard + reduce results and formats them into a scrollable report:
// per-shard exit code, outcome, the combined stdout/stderr (console logs + errors), and artifacts.
func (m model) fetchRunDetail(r runRow) tea.Cmd {
	mc := m
	return func() tea.Msg {
		title := "run " + shortID(r.id)
		if !validUUID(r.id) {
			return runDetailMsg{title, "invalid run id"}
		}
		ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
		defer cancel()
		q := `SELECT coalesce("ShardResultsJson",'[]'), coalesce("ReduceResultJson",'') ` +
			`FROM job_runs WHERE "Id"='` + r.id + `'`
		b, err := mc.kubectl(ctx, "-n", ns, "exec", "deploy/placecontext-db", "--",
			"psql", "-U", "postgres", "-d", "placecontext", "-At", "-F", "\x1f", "-c", q)
		if err != nil {
			return runDetailMsg{title, "could not load run: " + err.Error()}
		}
		parts := strings.SplitN(strings.TrimRight(string(b), "\n"), "\x1f", 2)
		shardsJSON := ""
		reduceJSON := ""
		if len(parts) > 0 {
			shardsJSON = parts[0]
		}
		if len(parts) > 1 {
			reduceJSON = parts[1]
		}
		body := renderRunDetail(r, shardsJSON, reduceJSON)
		body += mc.fetchRunArtifacts(ctx, r.id) // post-job outputs (MinIO) as openable links
		return runDetailMsg{title, body}
	}
}

// fetchRunArtifacts queries the post-job outputs (HTML report / chart / CSV / raw bundle) stored in
// MinIO for a run and renders them as a markdown section of portal links. The URLs are picked up by the
// detail view's link extractor, so they open with [o]/[1-9]. Returns "" when there are none.
func (m model) fetchRunArtifacts(ctx context.Context, runID string) string {
	if !validUUID(runID) {
		return ""
	}
	q := `SELECT "Kind","Title","Id" FROM job_run_artifacts WHERE "RunId"='` + runID + `' ORDER BY "CreatedAt"`
	b, err := m.kubectl(ctx, "-n", ns, "exec", "deploy/placecontext-db", "--",
		"psql", "-U", "postgres", "-d", "placecontext", "-At", "-F", "\t", "-c", q)
	if err != nil {
		return ""
	}
	var sb strings.Builder
	for _, ln := range strings.Split(strings.TrimSpace(string(b)), "\n") {
		if ln == "" {
			continue
		}
		f := strings.Split(ln, "\t")
		if len(f) < 3 {
			continue
		}
		if sb.Len() == 0 {
			sb.WriteString("\n## Post-job outputs\n\n")
		}
		sb.WriteString("- " + f[0] + " — " + f[1] + ": " + portalURL() + "runs/" + runID + "/artifacts/" + f[2] + "\n")
	}
	return sb.String()
}

// urlRe matches http(s) URLs in run output; trailing punctuation/brackets are trimmed by extractURLs.
var urlRe = regexp.MustCompile(`https?://[^\s<>"'` + "`" + `]+`)

// extractURLs pulls de-duplicated links from a run's output (in first-seen order) so the detail view
// can offer to open them. Trailing markdown/JSON punctuation is stripped from each match.
func extractURLs(body string) []string {
	seen := map[string]bool{}
	var out []string
	for _, u := range urlRe.FindAllString(body, -1) {
		u = strings.TrimRight(u, ".,);]}>\"'")
		if u != "" && !seen[u] {
			seen[u] = true
			out = append(out, u)
		}
	}
	return out
}

// openURL opens a link in the default browser, cross-platform and off the UI thread (mirrors openPortal).
func openURL(target string) tea.Cmd {
	return func() tea.Msg {
		var bin string
		switch {
		case commandExists("xdg-open"):
			bin = "xdg-open"
		case commandExists("open"):
			bin = "open"
		default:
			return flashMsg("no browser opener found — " + target)
		}
		cmd := exec.Command(bin, target)
		cmd.Env = childEnv()
		if err := cmd.Start(); err != nil {
			return flashMsg("open failed: " + err.Error())
		}
		return flashMsg("opened " + target)
	}
}

// renderRunDetail builds the markdown-ish body for a single run from its persisted JSON.
func renderRunDetail(r runRow, shardsJSON, reduceJSON string) string {
	var b strings.Builder
	b.WriteString("# run " + shortID(r.id) + "\n\n")
	b.WriteString("status: " + r.status + "   started: " + r.started)
	if r.duration != "" {
		b.WriteString("   took: " + r.duration)
	}
	b.WriteString("\n\n")

	var shards []shardJson
	if err := json.Unmarshal([]byte(shardsJSON), &shards); err != nil {
		b.WriteString("could not parse shard results: " + err.Error() + "\n")
	} else if len(shards) == 0 {
		b.WriteString("_(no shards recorded for this run)_\n")
	} else {
		for _, s := range shards {
			b.WriteString(fmt.Sprintf("## shard %d — %s (exit %d)\n\n", s.Index, s.Outcome, s.ExitCode))
			b.WriteString(renderStream("console (stdout/stderr)", s.Log))
			b.WriteString(renderStream("artifact", s.Artifact))
			b.WriteString(renderChart(s.Artifact))
			for _, a := range s.Artifacts {
				c := a.Content
				b.WriteString(renderStream("file: "+a.Name, &c))
				b.WriteString(renderChart(&c))
			}
		}
	}

	if strings.TrimSpace(reduceJSON) != "" {
		var rd reduceJson
		if err := json.Unmarshal([]byte(reduceJSON), &rd); err == nil {
			state := "failed"
			if rd.Succeeded {
				state = "succeeded"
			}
			b.WriteString(fmt.Sprintf("## reduce — %s (exit %d)\n\n", state, rd.ExitCode))
			b.WriteString(renderStream("console (stdout/stderr)", rd.Log))
			b.WriteString(renderStream("artifact", rd.Artifact))
			b.WriteString(renderChart(rd.Artifact))
		}
	}
	return b.String()
}

// renderStream prints a labelled fenced block, or a dim "none" note when empty.
func renderStream(label string, content *string) string {
	if content == nil || strings.TrimSpace(*content) == "" {
		return "_" + label + ": (none)_\n\n"
	}
	return "**" + label + "**\n\n```\n" + strings.TrimRight(*content, "\n") + "\n```\n\n"
}

// renderChart charts a JSON artifact's numeric series (see artchart.go); "" when it isn't one.
func renderChart(content *string) string {
	if content == nil {
		return ""
	}
	c := chartFromJSON(*content)
	if c == "" {
		return ""
	}
	return "**chart** _(from the artifact's JSON)_\n\n```\n" + strings.TrimRight(c, "\n") + "\n```\n\n"
}

func shortID(id string) string {
	id = strings.ReplaceAll(id, "-", "")
	if len(id) > 8 {
		return id[:8]
	}
	return id
}

// runsView lists the runs for the selected job; ⏎ opens a run's per-shard detail.
func (m model) runsView() string {
	var b strings.Builder
	b.WriteString(titleStyle.Render(fmt.Sprintf(" runs: %s (%d) ", m.runsJob.name, len(m.runs))) + "\n\n")
	if m.runsErr != "" {
		b.WriteString("  " + errStyle.Render("✗ "+m.runsErr) + "\n")
		b.WriteString("  " + dimStyle.Render("press [r] to retry · [esc] to go back") + "\n")
		return b.String()
	}
	if len(m.runs) == 0 {
		b.WriteString("  " + dimStyle.Render("no runs yet — press [R] on the dashboard to queue one.") + "\n")
		return b.String()
	}
	b.WriteString("  " + headStyle.Render(fmt.Sprintf("%-10s %-10s %-21s %-10s", "RUN", "STATUS", "STARTED", "TOOK")) + "\n")
	for i, r := range m.runs {
		st := r.status
		switch strings.ToLower(r.status) {
		case "succeeded", "success", "completed":
			st = okStyle.Render(r.status)
		case "failed", "error":
			st = errStyle.Render(r.status)
		default:
			st = warnStyle.Render(r.status)
		}
		line := fmt.Sprintf("%-10s %-19s %-21s %-10s", shortID(r.id), r.status, r.started, r.duration)
		if i == m.runCursor {
			b.WriteString(selStyle.Render("❯ "+line) + "\n")
		} else {
			// recompose with the colourised status so columns still align
			b.WriteString("  " + fmt.Sprintf("%-10s ", shortID(r.id)) + st +
				strings.Repeat(" ", max(1, 19-len(r.status))) +
				fmt.Sprintf("%-21s %-10s", r.started, r.duration) + "\n")
		}
	}
	b.WriteString("\n  " + dimStyle.Render("⏎ open run · per-shard console output, errors & artifacts") + "\n")
	return b.String()
}
