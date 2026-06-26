// Command pctl-tui is a reactive, full-screen terminal UI for managing PlaceContext
// clusters — the local k3d dev cluster (1 server + N agents) and prod k3s fleets.
//
// It auto-refreshes a live node/pod dashboard and drives the same orchestration logic
// as the `pctl` shell engine (it shells out to it for mutating actions), so there is a
// single source of truth. Build a static binary with `make` (see deploy/tui/Makefile)
// and scp it anywhere.
package main

import (
	"bufio"
	"bytes"
	"context"
	"encoding/json"
	"fmt"
	neturl "net/url"
	"os"
	"os/exec"
	"path/filepath"
	"strconv"
	"strings"
	"time"

	"github.com/charmbracelet/bubbles/spinner"
	"github.com/charmbracelet/bubbles/viewport"
	tea "github.com/charmbracelet/bubbletea"
	"github.com/charmbracelet/glamour"
	"github.com/charmbracelet/lipgloss"
)

// PlaceContext ASCII banner — the active font is chosen per theme by applyTheme (see themes.go).
var banner = bannerFonts[0]

// ── styles ────────────────────────────────────────────────────────────────────────────────────
var (
	cTeal   = lipgloss.Color("44")
	cGreen  = lipgloss.Color("42")
	cYellow = lipgloss.Color("220")
	cRed    = lipgloss.Color("196")
	cGray   = lipgloss.Color("245")
	cDim    = lipgloss.Color("239")

	bannerStyle = lipgloss.NewStyle().Foreground(cTeal).Bold(true)
	titleStyle  = lipgloss.NewStyle().Foreground(cTeal).Bold(true)
	dimStyle    = lipgloss.NewStyle().Foreground(cGray)
	okStyle     = lipgloss.NewStyle().Foreground(cGreen).Bold(true)
	warnStyle   = lipgloss.NewStyle().Foreground(cYellow).Bold(true)
	errStyle    = lipgloss.NewStyle().Foreground(cRed).Bold(true)
	boxStyle    = lipgloss.NewStyle().Border(lipgloss.RoundedBorder()).BorderForeground(cDim).Padding(0, 1)
	keyStyle    = lipgloss.NewStyle().Foreground(cTeal).Bold(true)
	headStyle   = lipgloss.NewStyle().Foreground(cGray).Bold(true).Underline(true)
	selStyle    = lipgloss.NewStyle().Background(lipgloss.Color("24")).Foreground(lipgloss.Color("231")).Bold(true)
)

// ── data model ──────────────────────────────────────────────────────────────────────────────────
type nodeRow struct {
	Name, Role, Status, Version string
}
type podRow struct {
	Name, Ready, Status, Node string
	Restarts                  int
}
type clusterState struct {
	nodes   []nodeRow
	pods    []podRow
	jobs    []jobRow
	jobsErr string
	hostUp  int // ready placecontext host pods
	hostTot int
	dbUp    bool
	reach   bool
	err     string
}

const (
	cluster = "placecontext"
	ns      = "placecontext"
)

type view int

const (
	viewDash view = iota
	viewLogs
	viewAction
	viewMenu
	viewMetrics
	viewConfirm
	viewMcp
	viewSearch
	viewSettings
	viewRuns      // run history for a selected job (list of runs)
	viewRunDetail // one run's per-shard output, errors, and artifacts
)

// menuItem is a choice in the "add node" (or any future) list picker.
type menuItem struct {
	label, desc string
	verb        string   // label shown while running
	args        []string // pctl args to run on select
}

type model struct {
	w, h       int
	state      clusterState
	kubeconfig string
	pctl       string
	updated    time.Time

	view     view
	sp       spinner.Model
	busy     bool
	busyVerb string
	out      viewport.Model
	logs     viewport.Model
	logTitle string
	quitting bool

	// dashboard row selection
	sel    []selItem
	cursor int

	// list picker (e.g. add node)
	menuTitle  string
	menu       []menuItem
	menuCursor int

	flash string // transient one-line notice under the health line

	// metrics view (line graphs)
	cpuHist    []float64 // total CPU millicores across workload pods, over time
	memHist    []float64 // total memory MiB, over time
	metricsErr string

	// destructive-action confirmation
	prevView    view
	confirmText string
	confirmVerb string
	confirmArgs []string

	// cluster panel (top of the dashboard)
	clAngX float64 // reserved viewing tilt
	clAngY float64 // reserved viewing yaw
	clZoom float64
	clSpin bool
	orbit  float64 // orbital animation phase (advances while clSpin)

	// MCP tool-call log
	mcp       []mcpRow
	mcpErr    string
	mcpCursor int

	// search (decisions / context / activity — the knowledge graph's contents)
	searchQuery string

	// per-job settings view (checkboxes)
	setJob    jobRow // the job whose settings are being edited
	setCursor int

	// run-history drill-down: job → runs list → one run's per-shard detail
	runsJob   jobRow
	runs      []runRow
	runCursor int
	runsErr   string // sticky error for the runs list (e.g. query failed), shown in runsView

	loading bool // a data fetch (logs/mcp/metrics/search) is in flight → show a loading box
}

// ── messages ──────────────────────────────────────────────────────────────────────────────────
type tickMsg time.Time
type stateMsg clusterState
type logsMsg struct{ title, body string }
type actionDoneMsg struct {
	verb, output string
	err          error
}

type flashMsg string
type metricsMsg struct {
	cpu, mem float64
	err      string
}
type metricsTickMsg time.Time
type clusterTickMsg time.Time

func metricsTick() tea.Cmd {
	return tea.Tick(2*time.Second, func(t time.Time) tea.Msg { return metricsTickMsg(t) })
}

func clusterTick() tea.Cmd {
	return tea.Tick(90*time.Millisecond, func(t time.Time) tea.Msg { return clusterTickMsg(t) })
}

// selItem is one keyboard-selectable row in the dashboard (a node or a pod).
type selItem struct{ kind, name, node string } // kind: "node" | "pod"

func tick() tea.Cmd {
	// Poll cluster state ~1.5s so the dashboard reflects events (pods/jobs appearing, status
	// changes) close to real time.
	return tea.Tick(1500*time.Millisecond, func(t time.Time) tea.Msg { return tickMsg(t) })
}

// ── kubectl plumbing ────────────────────────────────────────────────────────────────────────────
func (m model) kubectl(ctx context.Context, args ...string) ([]byte, error) {
	full := append([]string{"--kubeconfig", m.kubeconfig, "--context", "k3d-" + cluster}, args...)
	cmd := exec.CommandContext(ctx, "kubectl", full...)
	cmd.Env = childEnv()
	var stderr bytes.Buffer
	cmd.Stderr = &stderr
	out, err := cmd.Output()
	// Surface stderr in the error so callers (and the user) see the real cause — e.g. a psql
	// "column does not exist" rather than a bare "exit status 1".
	if err != nil {
		if msg := strings.TrimSpace(stderr.String()); msg != "" {
			return out, fmt.Errorf("%s: %w", msg, err)
		}
	}
	return out, err
}

// childEnv ensures ~/.local/bin (where k3d/kubectl may live) is on PATH for children.
func childEnv() []string {
	env := os.Environ()
	home, _ := os.UserHomeDir()
	local := filepath.Join(home, ".local", "bin")
	for i, e := range env {
		if strings.HasPrefix(e, "PATH=") {
			if !strings.Contains(e, local) {
				env[i] = "PATH=" + local + ":" + strings.TrimPrefix(e, "PATH=")
			}
			return env
		}
	}
	return append(env, "PATH="+local)
}

func (m model) fetchState() tea.Cmd {
	mc := m
	return func() tea.Msg {
		ctx, cancel := context.WithTimeout(context.Background(), 8*time.Second)
		defer cancel()
		st := clusterState{}
		nb, err := mc.kubectl(ctx, "get", "nodes", "-o", "json")
		if err != nil {
			st.err = "cluster not reachable — press [u] to bring the dev cluster up"
			return stateMsg(st)
		}
		st.reach = true
		st.nodes = parseNodes(nb)
		pb, err := mc.kubectl(ctx, "-n", ns, "get", "pods", "-o", "json")
		if err == nil {
			st.pods = parsePods(pb)
			for _, p := range st.pods {
				if strings.HasPrefix(p.Name, "placecontext-db") {
					st.dbUp = st.dbUp || p.Status == "Running"
				} else if strings.HasPrefix(p.Name, "placecontext-") {
					st.hostTot++
					if p.Ready == "1/1" && p.Status == "Running" {
						st.hostUp++
					}
				}
			}
		}
		// Always query jobs while the cluster is reachable (don't gate on dbUp detection, which can
		// flicker) so a newly-added job shows up on the next ~1.5s refresh.
		st.jobs, st.jobsErr = mc.queryJobs(ctx)
		return stateMsg(st)
	}
}

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

// armKill prepares the confirmation modal for deleting the selected node/pod/job.
func (m *model) armKill(it selItem) {
	switch it.kind {
	case "pod":
		m.confirmText = "Delete pod \"" + it.name + "\"?  Its controller will reschedule a replacement."
	case "node":
		m.confirmText = "Remove node \"" + it.name + "\" from the cluster?  Its workloads will reschedule."
	case "job":
		m.confirmText = "Delete job \"" + it.name + "\" and its entire run history?  This cannot be undone."
	}
	m.confirmVerb = "kill " + it.kind + " " + it.name
	m.confirmArgs = []string{"kill", it.kind, it.name, "--yes"}
}

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

type jobRow struct{ id, tenant, name, source, conc, egress, updated, timeout string }

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

func (m model) queryJobs(ctx context.Context) ([]jobRow, string) {
	const base = `"Id", "TenantId", "Name", "MapSourceKind", "ConcurrencyLimit", "AllowNetworkEgress", "UpdatedAt"`
	run := func(sel string) ([]byte, error) {
		q := `SELECT ` + sel + ` FROM jobs ORDER BY "UpdatedAt" DESC LIMIT 100`
		return m.kubectl(ctx, "-n", ns, "exec", "deploy/placecontext-db", "--",
			"psql", "-U", "postgres", "-d", "placecontext", "-At", "-F", "\t", "-c", q)
	}
	// TimeoutSeconds is a newer column; if the DB is behind the app code (migration not yet
	// applied) fall back to a query without it so the jobs list still works — the migration
	// banner (checkMigrations) tells the user to deploy.
	b, err := run(base + `, "TimeoutSeconds"`)
	if err != nil && strings.Contains(err.Error(), "does not exist") {
		b, err = run(base) // 7 cols; the row padding below defaults TimeoutSeconds to 300
	}
	if err != nil {
		return nil, "could not query jobs: " + err.Error()
	}
	var rows []jobRow
	for _, ln := range strings.Split(strings.TrimSpace(string(b)), "\n") {
		if ln == "" {
			continue
		}
		f := strings.Split(ln, "\t")
		for len(f) < 8 {
			f = append(f, "")
		}
		eg := "no"
		if f[5] == "t" {
			eg = "yes"
		}
		upd := f[6]
		if len(upd) > 19 {
			upd = upd[:19]
		}
		to := f[7]
		if to == "" {
			to = "300"
		}
		rows = append(rows, jobRow{f[0], f[1], f[2], f[3], f[4], eg, upd, to})
	}
	return rows, ""
}

// runJobCmd enqueues a manual run of a job by inserting a row into the durable pending_job_runs queue,
// exactly like a trigger would. The in-cluster scheduler claims it (FOR UPDATE SKIP LOCKED) on any
// replica and dispatches it as a Kubernetes Job — so the TUI never runs work on its own thread.
func (m model) runJobCmd(j jobRow) tea.Cmd {
	mc := m
	return func() tea.Msg {
		if j.id == "" || j.tenant == "" {
			return flashMsg("could not run job — missing id/tenant (refresh and retry)")
		}
		ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
		defer cancel()
		q := `INSERT INTO pending_job_runs ` +
			`("Id","TenantId","JobId","TriggerId","TriggerName","Payload","EnqueuedAt") VALUES ` +
			`(gen_random_uuid(), '` + j.tenant + `', '` + j.id + `', ` +
			`'00000000-0000-0000-0000-000000000000', 'tui-manual', NULL, now())`
		if _, err := mc.kubectl(ctx, "-n", ns, "exec", "deploy/placecontext-db", "--",
			"psql", "-U", "postgres", "-d", "placecontext", "-c", q); err != nil {
			return flashMsg("run failed: " + err.Error())
		}
		return flashMsg("queued run of \"" + j.name + "\" — watch the jobs list / runs")
	}
}

// jobSetting is one toggleable (checkbox) job setting in the settings view ([s]). Each maps to a
// boolean column on the jobs table; toggling persists immediately and takes effect on the next run.
type jobSetting struct {
	label, desc, column string
	get                 func(jobRow) bool
}

func jobSettings() []jobSetting {
	return []jobSetting{
		{"Allow network egress",
			"Let this job's containers reach the network. When off, a deny-all NetworkPolicy isolates the run.",
			"AllowNetworkEgress", func(j jobRow) bool { return j.egress == "yes" }},
	}
}

type jobSettingMsg struct {
	column string
	val    bool
	flash  string
}

// toggleJobSettingCmd flips a boolean job setting in the jobs table via psql, off the UI thread.
func (m model) toggleJobSettingCmd(j jobRow, s jobSetting, val bool) tea.Cmd {
	mc := m
	return func() tea.Msg {
		if j.id == "" {
			return flashMsg("could not update setting — missing job id (refresh and retry)")
		}
		ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
		defer cancel()
		b := "false"
		state := "off"
		if val {
			b, state = "true", "on"
		}
		q := `UPDATE jobs SET "` + s.column + `"=` + b + `, "UpdatedAt"=now() WHERE "Id"='` + j.id + `'`
		if _, err := mc.kubectl(ctx, "-n", ns, "exec", "deploy/placecontext-db", "--",
			"psql", "-U", "postgres", "-d", "placecontext", "-c", q); err != nil {
			return flashMsg(s.label + " toggle failed: " + err.Error())
		}
		return jobSettingMsg{s.column, val, s.label + ": " + state}
	}
}

// timeout adjustment bounds (mirrors Job.DefaultTimeoutSeconds / MaxTimeoutSeconds on the server).
const (
	timeoutStep = 30
	timeoutMin  = 30
	timeoutMax  = 3600
)

type jobTimeoutMsg struct {
	val   int
	flash string
}

// adjustTimeoutCmd persists a new per-job timeout (clamped) to the jobs table via psql, off the UI
// thread. delta is added to the current value and snapped to the [timeoutMin, timeoutMax] range.
func (m model) adjustTimeoutCmd(j jobRow, delta int) tea.Cmd {
	mc := m
	return func() tea.Msg {
		if j.id == "" {
			return flashMsg("could not update timeout — missing job id (refresh and retry)")
		}
		cur, err := strconv.Atoi(strings.TrimSpace(j.timeout))
		if err != nil || cur <= 0 {
			cur = 300
		}
		next := cur + delta
		if next < timeoutMin {
			next = timeoutMin
		}
		if next > timeoutMax {
			next = timeoutMax
		}
		if next == cur {
			return jobTimeoutMsg{cur, "timeout: " + strconv.Itoa(cur) + "s (limit reached)"}
		}
		ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
		defer cancel()
		q := `UPDATE jobs SET "TimeoutSeconds"=` + strconv.Itoa(next) + `, "UpdatedAt"=now() WHERE "Id"='` + j.id + `'`
		if _, err := mc.kubectl(ctx, "-n", ns, "exec", "deploy/placecontext-db", "--",
			"psql", "-U", "postgres", "-d", "placecontext", "-c", q); err != nil {
			return flashMsg("timeout update failed: " + err.Error())
		}
		return jobTimeoutMsg{next, "timeout: " + strconv.Itoa(next) + "s"}
	}
}

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
		if j.id == "" {
			return runsMsg{nil, "missing job id (refresh and retry)"}
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
		if r.id == "" {
			return runDetailMsg{title, "missing run id"}
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
		return runDetailMsg{title, renderRunDetail(r, shardsJSON, reduceJSON)}
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
			for _, a := range s.Artifacts {
				c := a.Content
				b.WriteString(renderStream("file: "+a.Name, &c))
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

func shortID(id string) string {
	id = strings.ReplaceAll(id, "-", "")
	if len(id) > 8 {
		return id[:8]
	}
	return id
}

// addNodeMenu lists the "add node" choices (dev k3d nodes + the prod join command).
func addNodeMenu() []menuItem {
	return []menuItem{
		{"k3d worker node (agent)", "Add a worker to the local dev cluster", "add k3d agent", []string{"dev", "add-node", "--role", "agent"}},
		{"k3d server node (control-plane)", "Add a control-plane node to the dev cluster", "add k3d server", []string{"dev", "add-node", "--role", "server"}},
		{"prod worker join command", "Print the k3s command to join a worker on another machine", "join-cmd", []string{"join-cmd"}},
	}
}

// runAction streams `pctl <args...>` output into the action pane.
func (m model) runAction(verb string, args ...string) tea.Cmd {
	pctl := m.pctl
	return func() tea.Msg {
		cmd := exec.Command(pctl, args...)
		cmd.Env = childEnv()
		var sb strings.Builder
		stdout, _ := cmd.StdoutPipe()
		cmd.Stderr = cmd.Stdout
		if err := cmd.Start(); err != nil {
			return actionDoneMsg{verb, "", err}
		}
		sc := bufio.NewScanner(stdout)
		for sc.Scan() {
			sb.WriteString(sc.Text() + "\n")
		}
		err := cmd.Wait()
		return actionDoneMsg{verb, sb.String(), err}
	}
}

// ── parsing ─────────────────────────────────────────────────────────────────────────────────────
type k8sList struct {
	Items []struct {
		Metadata struct {
			Name   string            `json:"name"`
			Labels map[string]string `json:"labels"`
		} `json:"metadata"`
		Spec struct {
			NodeName string `json:"nodeName"`
		} `json:"spec"`
		Status struct {
			Phase      string `json:"phase"`
			Conditions []struct {
				Type, Status string
			} `json:"conditions"`
			NodeInfo struct {
				KubeletVersion string `json:"kubeletVersion"`
			} `json:"nodeInfo"`
			ContainerStatuses []struct {
				Ready        bool `json:"ready"`
				RestartCount int  `json:"restartCount"`
			} `json:"containerStatuses"`
		} `json:"status"`
	} `json:"items"`
}

func parseNodes(b []byte) []nodeRow {
	var l k8sList
	if json.Unmarshal(b, &l) != nil {
		return nil
	}
	var rows []nodeRow
	for _, it := range l.Items {
		role := "agent"
		if _, ok := it.Metadata.Labels["node-role.kubernetes.io/control-plane"]; ok {
			role = "server"
		}
		status := "NotReady"
		for _, c := range it.Status.Conditions {
			if c.Type == "Ready" && c.Status == "True" {
				status = "Ready"
			}
		}
		rows = append(rows, nodeRow{it.Metadata.Name, role, status, it.Status.NodeInfo.KubeletVersion})
	}
	return rows
}

func parsePods(b []byte) []podRow {
	var l k8sList
	if json.Unmarshal(b, &l) != nil {
		return nil
	}
	var rows []podRow
	for _, it := range l.Items {
		ready, total, restarts := 0, len(it.Status.ContainerStatuses), 0
		for _, cs := range it.Status.ContainerStatuses {
			if cs.Ready {
				ready++
			}
			if cs.RestartCount > restarts {
				restarts = cs.RestartCount
			}
		}
		rows = append(rows, podRow{
			Name:     it.Metadata.Name,
			Ready:    fmt.Sprintf("%d/%d", ready, total),
			Status:   it.Status.Phase,
			Node:     it.Spec.NodeName,
			Restarts: restarts,
		})
	}
	return rows
}

// ── bubbletea ─────────────────────────────────────────────────────────────────────────────────
func initialModel() model {
	sp := spinner.New()
	sp.Spinner = spinner.Dot
	sp.Style = lipgloss.NewStyle().Foreground(cTeal)
	return model{
		sp:         sp,
		kubeconfig: resolveKubeconfig(),
		pctl:       findPctl(),
		out:        viewport.New(80, 14),
		logs:       viewport.New(80, 14),
		clZoom:     1.0,
		clSpin:     true,
	}
}

func (m model) Init() tea.Cmd {
	return tea.Batch(m.sp.Tick, m.fetchState(), tick(), clusterTick())
}

func (m model) Update(msg tea.Msg) (tea.Model, tea.Cmd) {
	switch msg := msg.(type) {
	case tea.WindowSizeMsg:
		m.w, m.h = msg.Width, msg.Height
		paneH := max(6, m.h-18)
		m.out.Width, m.out.Height = msg.Width-4, paneH
		m.logs.Width, m.logs.Height = msg.Width-4, paneH
		return m, nil

	case tea.KeyMsg:
		key := msg.String()

		// Search input captures typing first (so q/b/esc aren't treated as global shortcuts).
		if m.view == viewSearch {
			switch key {
			case "esc", "ctrl+c":
				m.view = viewDash
			case "enter":
				m.loading = true
				return m, m.fetchSearch(m.searchQuery)
			case "backspace":
				if r := []rune(m.searchQuery); len(r) > 0 {
					m.searchQuery = string(r[:len(r)-1])
				}
			default:
				if len(key) == 1 { // a single printable rune
					m.searchQuery += key
				}
			}
			return m, nil
		}

		switch key {
		case "q", "ctrl+c":
			m.quitting = true
			return m, tea.Quit
		case "b", "esc":
			if m.busy {
				return m, nil
			}
			// confirm and the logs detail return to wherever they were opened from; the run drill-down
			// steps back one level (detail → list → dashboard); everything else returns to the dashboard.
			switch {
			case m.view == viewConfirm || m.view == viewLogs:
				m.view = m.prevView
			case m.view == viewRunDetail:
				m.view = viewRuns
			case m.view == viewRuns:
				m.view = viewDash
			default:
				m.view = viewDash
			}
			return m, nil
		}

		// destructive-action confirmation gate
		if m.view == viewConfirm {
			switch key {
			case "y", "Y":
				m.busy, m.busyVerb, m.view = true, m.confirmVerb, viewAction
				m.out.SetContent("")
				// The spinner ticks on a single chain started in Init — don't start another (racing
				// chains drop each other's ticks via tag-dedup and can freeze the spinner).
				return m, m.runAction(m.confirmVerb, m.confirmArgs...)
			case "n", "N":
				m.view = m.prevView
			}
			return m, nil
		}

		// menu (list picker) navigation
		if m.view == viewMenu {
			switch key {
			case "up", "k":
				if m.menuCursor > 0 {
					m.menuCursor--
				}
			case "down", "j":
				if m.menuCursor < len(m.menu)-1 {
					m.menuCursor++
				}
			case "enter":
				if m.menuCursor < len(m.menu) {
					it := m.menu[m.menuCursor]
					m.busy, m.busyVerb, m.view = true, it.verb, viewAction
					m.out.SetContent("")
					return m, m.runAction(it.verb, it.args...)
				}
			}
			return m, nil
		}

		// MCP call list — navigate and drill into a call's request/response (like pod logs)
		if m.view == viewMcp {
			switch key {
			case "up", "k":
				if m.mcpCursor > 0 {
					m.mcpCursor--
				}
			case "down", "j":
				if m.mcpCursor < len(m.mcp)-1 {
					m.mcpCursor++
				}
			case "enter":
				if m.mcpCursor < len(m.mcp) {
					m.prevView = viewMcp
					m.view = viewLogs
					m.logs.SetContent("")
					m.loading = true
					return m, m.fetchMcpDetail(m.mcp[m.mcpCursor])
				}
			case "r":
				m.loading = true
				return m, m.fetchMcp()
			}
			return m, nil
		}

		// per-job settings — checkboxes toggled with space; the timeout row (last) is adjusted with
		// ←/→ (or -/+). All changes persist to the jobs table off the UI thread.
		if m.view == viewSettings {
			items := jobSettings()
			timeoutRow := len(items) // the timeout sits one past the checkboxes
			switch key {
			case "up", "k":
				if m.setCursor > 0 {
					m.setCursor--
				}
			case "down", "j":
				if m.setCursor < timeoutRow {
					m.setCursor++
				}
			case " ", "enter":
				if m.setCursor < len(items) {
					s := items[m.setCursor]
					cur := s.get(m.setJob)
					return m, m.toggleJobSettingCmd(m.setJob, s, !cur)
				}
			case "right", "l", "+", "=":
				if m.setCursor == timeoutRow {
					return m, m.adjustTimeoutCmd(m.setJob, timeoutStep)
				}
			case "left", "h", "-", "_":
				if m.setCursor == timeoutRow {
					return m, m.adjustTimeoutCmd(m.setJob, -timeoutStep)
				}
			}
			return m, nil
		}

		// run-history list navigation (drill into a run with ⏎, handled in the main enter case)
		if m.view == viewRuns {
			switch key {
			case "up", "k":
				if m.runCursor > 0 {
					m.runCursor--
				}
			case "down", "j":
				if m.runCursor < len(m.runs)-1 {
					m.runCursor++
				}
			case "r":
				m.loading = true
				return m, m.fetchRuns(m.runsJob)
			case "enter":
				// Open the highlighted run's detail. (This must live here: the viewRuns block
				// returns early below, so the generic enter case further down never sees it.)
				if m.runCursor < len(m.runs) {
					m.view = viewRunDetail
					m.logs.SetContent("")
					m.loading = true
					return m, m.fetchRunDetail(m.runs[m.runCursor])
				}
				m.flash = "no run to open"
			}
			return m, nil
		}

		switch key {
		case "up", "k":
			if m.view == viewDash && m.cursor > 0 {
				m.cursor--
			}
		case "down", "j":
			if m.view == viewDash && m.cursor < len(m.sel)-1 {
				m.cursor++
			}
		case "+", "=":
			if m.view == viewDash {
				m.clZoom *= 1.1
			}
		case "-", "_":
			if m.view == viewDash {
				m.clZoom /= 1.1
			}
		case " ":
			if m.view == viewDash {
				m.clSpin = !m.clSpin
			}
		case "enter":
			if m.view == viewDash && m.cursor < len(m.sel) {
				it := m.sel[m.cursor]
				// Jobs drill into run history (list → per-run detail); pods/nodes show logs/describe.
				if it.kind == "job" {
					for _, j := range m.state.jobs {
						if j.name == it.name {
							m.runsJob, m.runCursor, m.view = j, 0, viewRuns
							m.loading = true
							return m, m.fetchRuns(j)
						}
					}
					return m, nil
				}
				m.prevView = viewDash
				m.view = viewLogs
				m.logs.SetContent("")
				m.loading = true
				return m, m.fetchLogsFor(it)
			}
			// (Runs-list ⏎ is handled in the viewRuns block above, which returns early.)
		case "a":
			// One keypress adds a worker computer to the cluster — no jargon, no choices.
			if !m.busy {
				m.busy, m.busyVerb, m.view = true, "adding a worker", viewAction
				m.out.SetContent("")
				return m, m.runAction("adding a worker", "dev", "add-node", "--role", "agent")
			}
			return m, nil
		case "g":
			if !m.busy {
				m.view = viewMetrics
				m.loading = true
				return m, tea.Batch(m.fetchMetrics(), metricsTick())
			}
			return m, nil
		case "c":
			themeIdx++
			applyTheme(themeIdx)
			m.flash = "theme: " + themeName()
			return m, nil
		case "/":
			if !m.busy {
				m.view, m.searchQuery = viewSearch, ""
				m.logs.SetContent("type a query (decisions · context · activity), then enter")
			}
			return m, nil
		case "p":
			return m, m.openPortal()
		case "$":
			return m, m.openBilling()
		case "m":
			if m.view == viewDash && !m.busy {
				m.view, m.mcpCursor = viewMcp, 0
				m.loading = true
				return m, m.fetchMcp()
			}
			return m, nil
		case "x":
			// Pods and nodes are read-only from the TUI; only jobs can be deleted here.
			if m.view == viewDash && !m.busy && m.cursor < len(m.sel) {
				if it := m.sel[m.cursor]; it.kind == "job" {
					m.prevView = viewDash
					m.armKill(it)
					m.view = viewConfirm
				} else {
					m.flash = "pods & nodes are read-only — only jobs can be killed"
				}
			}
			return m, nil
		case "R":
			// Run the selected job — enqueue a manual run drained by the in-cluster scheduler.
			if m.view == viewDash && m.cursor < len(m.sel) {
				if it := m.sel[m.cursor]; it.kind == "job" {
					for _, j := range m.state.jobs {
						if j.name == it.name {
							m.flash = "queuing run…"
							return m, m.runJobCmd(j)
						}
					}
				} else {
					m.flash = "select a job to run it ([R])"
				}
			}
			return m, nil
		case "s":
			// Open per-job settings (checkboxes) for the selected job.
			if m.view == viewDash && m.cursor < len(m.sel) {
				if it := m.sel[m.cursor]; it.kind == "job" {
					for _, j := range m.state.jobs {
						if j.name == it.name {
							m.setJob, m.setCursor, m.prevView, m.view = j, 0, viewDash, viewSettings
							return m, nil
						}
					}
				} else {
					m.flash = "select a job to edit its settings ([s])"
				}
			}
			return m, nil
		case "r":
			if m.view == viewLogs && m.cursor < len(m.sel) {
				m.loading = true
				return m, m.fetchLogsFor(m.sel[m.cursor])
			}
			if m.view == viewMcp {
				m.loading = true
				return m, m.fetchMcp()
			}
			return m, m.fetchState()
		case "l":
			m.prevView = viewDash
			m.view = viewLogs
			m.logs.SetContent("")
			m.loading = true
			return m, m.fetchLogs()
		}

	case tickMsg:
		var cmd tea.Cmd
		if m.view == viewDash {
			cmd = m.fetchState()
		}
		m.flash = "" // flash lives for one tick (~3s)
		return m, tea.Batch(cmd, tick())

	case flashMsg:
		m.flash = string(msg)
		return m, nil

	case jobSettingMsg:
		if msg.column == "AllowNetworkEgress" {
			if msg.val {
				m.setJob.egress = "yes"
			} else {
				m.setJob.egress = "no"
			}
		}
		m.flash = msg.flash
		return m, m.fetchState() // refresh the jobs list so the change shows everywhere

	case jobTimeoutMsg:
		m.setJob.timeout = strconv.Itoa(msg.val)
		m.flash = msg.flash
		return m, m.fetchState()

	case runsMsg:
		m.loading = false
		m.runs = msg.rows
		m.runsErr = msg.err
		if msg.err != "" {
			m.flash = msg.err
		}
		if m.runCursor >= len(m.runs) {
			m.runCursor = max(0, len(m.runs)-1)
		}
		return m, nil

	case runDetailMsg:
		m.loading = false
		m.logTitle = msg.title
		m.logs.SetContent(renderMarkdown(msg.body, m.logs.Width))
		m.logs.GotoTop()
		return m, nil

	case metricsTickMsg:
		if m.view != viewMetrics {
			return m, nil // stop sampling when the metrics view isn't shown
		}
		// Refresh silently: keep the graphs on screen while the next sample fetches in the
		// background (the initial open sets loading once; periodic ticks must not re-blank it).
		return m, tea.Batch(m.fetchMetrics(), metricsTick())

	case metricsMsg:
		m.loading = false
		if msg.err != "" {
			m.metricsErr = msg.err
			return m, nil
		}
		m.metricsErr = ""
		m.cpuHist = appendCap(m.cpuHist, msg.cpu, 240)
		m.memHist = appendCap(m.memHist, msg.mem, 240)
		return m, nil

	case clusterTickMsg:
		// Keep the loop alive across view changes (returning nil would stop it for good, so
		// the animation wouldn't resume when you navigate back to the dashboard). Only advance
		// the phase while the dashboard is visible and spin is on.
		if m.view == viewDash && m.clSpin {
			m.orbit += 0.012 // slow, elegant orbital motion
		}
		return m, clusterTick()

	case stateMsg:
		// A completed state fetch always ends any loading state — backstop so the loader can never
		// freeze on screen (some action paths dispatch fetchState and rely on this to clear it).
		m.loading = false
		m.state = clusterState(msg)
		m.updated = time.Now()
		sel := make([]selItem, 0, len(m.state.nodes)+len(m.state.pods)+len(m.state.jobs))
		for _, n := range m.state.nodes {
			sel = append(sel, selItem{"node", n.Name, ""})
		}
		for _, p := range m.state.pods {
			sel = append(sel, selItem{"pod", p.Name, p.Node})
		}
		for _, j := range m.state.jobs {
			sel = append(sel, selItem{"job", j.name, ""})
		}
		m.sel = sel
		if m.cursor >= len(sel) {
			m.cursor = max(0, len(sel)-1)
		}
		return m, nil

	case logsMsg:
		m.loading = false
		m.logTitle = msg.title
		m.logs.SetContent(colorizeLogs(msg.body))
		m.logs.GotoTop()
		return m, nil

	case searchMsg:
		m.loading = false
		m.logs.SetContent(renderMarkdown(msg.body, m.logs.Width))
		m.logs.GotoTop()
		return m, nil

	case mcpMsg:
		m.loading = false
		m.mcp, m.mcpErr = msg.rows, msg.err
		if m.mcpCursor >= len(m.mcp) {
			m.mcpCursor = max(0, len(m.mcp)-1)
		}
		return m, nil

	case actionDoneMsg:
		m.busy = false
		body := msg.output
		if msg.err != nil {
			body += "\n" + errStyle.Render("error: "+msg.err.Error())
		} else {
			body += "\n" + okStyle.Render("✓ "+msg.verb+" complete — press [b] for dashboard")
		}
		m.out.SetContent(body)
		m.out.GotoBottom()
		return m, m.fetchState()

	case spinner.TickMsg:
		var cmd tea.Cmd
		m.sp, cmd = m.sp.Update(msg)
		return m, cmd
	}

	// route scrolling to the active pane
	var cmd tea.Cmd
	switch m.view {
	case viewLogs, viewRunDetail:
		m.logs, cmd = m.logs.Update(msg)
	case viewAction:
		m.out, cmd = m.out.Update(msg)
	}
	return m, cmd
}

// ── view ────────────────────────────────────────────────────────────────────────────────────────
func (m model) View() string {
	if m.quitting {
		return ""
	}
	// A data fetch in flight takes over the whole screen with a centered loader.
	if m.loading {
		box := boxStyle.Render(m.sp.View() + "  " + titleStyle.Render("loading…"))
		return lipgloss.Place(max(m.w, 1), max(m.h, 1), lipgloss.Center, lipgloss.Center, box)
	}
	var b strings.Builder
	b.WriteString(bannerStyle.Render(banner) + "\n")
	b.WriteString(dimStyle.Render("  hosted multi-tenant context · MCP + portal") + "\n\n")
	b.WriteString(m.healthLine() + "\n")
	if a := m.alerts(); a != "" {
		b.WriteString(a)
	}
	if m.flash != "" {
		b.WriteString("  " + okStyle.Render("✓ "+m.flash) + "\n")
	} else {
		b.WriteString("\n")
	}

	switch m.view {
	case viewLogs:
		b.WriteString(titleStyle.Render(" logs: "+m.logTitle+" ") + "\n")
		b.WriteString(boxStyle.Render(m.logs.View()) + "\n")
	case viewAction:
		head := titleStyle.Render(" " + m.busyVerb + " ")
		if m.busy {
			head += " " + m.sp.View() + dimStyle.Render(" working…")
		}
		b.WriteString(head + "\n")
		b.WriteString(boxStyle.Render(m.out.View()) + "\n")
	case viewMenu:
		b.WriteString(m.menuView())
	case viewMetrics:
		b.WriteString(m.metricsView())
	case viewConfirm:
		b.WriteString(m.confirmView())
	case viewMcp:
		b.WriteString(m.mcpView())
	case viewSearch:
		b.WriteString(m.searchView())
	case viewSettings:
		b.WriteString(m.settingsView())
	case viewRuns:
		b.WriteString(m.runsView())
	case viewRunDetail:
		b.WriteString(titleStyle.Render(" "+m.logTitle+" ") + dimStyle.Render("  job: "+m.runsJob.name) + "\n")
		b.WriteString(boxStyle.Render(m.logs.View()) + "\n")
	default:
		b.WriteString(m.dashboard())
	}

	b.WriteString("\n" + m.footer())
	return b.String()
}

func (m model) healthLine() string {
	s := m.state
	if !s.reach {
		return "  " + warnStyle.Render("● no dev cluster") + dimStyle.Render("   "+s.err)
	}
	dot := func(ok bool, label string) string {
		if ok {
			return okStyle.Render("● ") + label
		}
		return warnStyle.Render("● ") + label
	}
	host := fmt.Sprintf("%d/%d host", s.hostUp, s.hostTot)
	parts := []string{
		dot(true, fmt.Sprintf("%d nodes", len(s.nodes))),
		dot(s.hostUp == s.hostTot && s.hostTot > 0, host),
		dot(s.dbUp, "db"),
	}
	upd := ""
	if !m.updated.IsZero() {
		upd = dimStyle.Render("   updated " + m.updated.Format("15:04:05"))
	}
	return "  ▸ dev cluster '" + cluster + "'   " + strings.Join(parts, "   ") + upd
}

// alerts renders error/warning lines at the top of the dashboard (cluster down, crashing/pending pods,
// db not ready). Empty string when all is well.
func (m model) alerts() string {
	s := m.state
	var out strings.Builder
	if !s.reach {
		return "" // first-run: the friendly setup guide covers this, no scary banner
	}
	n := 0
	warn := func(msg string) {
		if n < 3 {
			out.WriteString("  " + warnStyle.Render("▲ "+msg) + "\n")
		}
		n++
	}
	if s.hostTot > 0 && s.hostUp < s.hostTot {
		warn(fmt.Sprintf("host: %d/%d replicas ready", s.hostUp, s.hostTot))
	}
	if !s.dbUp {
		warn("database not ready")
	}
	for _, p := range s.pods {
		if p.Status != "Running" && p.Status != "Succeeded" {
			warn("pod " + trunc(p.Name, 28) + " " + p.Status)
		} else if p.Restarts > 0 {
			warn(fmt.Sprintf("pod %s restarted ×%d", trunc(p.Name, 28), p.Restarts))
		}
	}
	if n > 3 {
		out.WriteString("  " + dimStyle.Render(fmt.Sprintf("…and %d more", n-3)) + "\n")
	}
	return out.String()
}

// setupGuide is the friendly empty state shown before any cluster exists.
func (m model) setupGuide() string {
	step := func(k, s string) string { return "   " + keyStyle.Render(k) + "  " + s + "\n" }
	var b strings.Builder
	b.WriteString(titleStyle.Render(" Welcome to PlaceContext ") + "\n\n")
	b.WriteString("  No cluster yet. Let's get you running:\n\n")
	b.WriteString(step("[u]", "Create your cluster (one key — sets up everything locally)"))
	b.WriteString(step("[p]", "Open the portal once it's up"))
	b.WriteString(step("[$]", "Manage your subscription"))
	b.WriteString(step("[q]", "Quit"))
	b.WriteString("\n  " + dimStyle.Render("Tip: see docs/SETUP.md for the full guide.") + "\n")
	return b.String()
}

func (m model) dashboard() string {
	if !m.state.reach {
		return m.setupGuide()
	}
	// Side-by-side: cluster on the left, selectable node/pod/job list on the right. This keeps the
	// page short (so the banner/footer don't scroll off as more items are added).
	rows := m.h - 11 // height available below the banner/health, above the footer
	if rows < 8 {
		rows = 8
	} else if rows > 26 {
		rows = 26
	}
	leftW := m.w / 2 // cluster ~half; the node/pod/job list gets the wider remaining half
	if leftW < 36 {
		leftW = 36
	}
	if leftW > m.w-40 {
		leftW = max(24, m.w-40)
	}
	left := lipgloss.NewStyle().Width(leftW).Render(m.clusterPanel(leftW-2, rows))
	right := lipgloss.NewStyle().Width(m.w - leftW - 2).Render(m.listBody())
	return lipgloss.JoinHorizontal(lipgloss.Top, left, "  ", right)
}

// listBody renders the selectable nodes + pods + jobs tables shown beneath the cluster.
func (m model) listBody() string {
	var b strings.Builder
	gi := 0 // global selectable index (nodes then pods), matches m.sel order

	// nodes
	b.WriteString("  " + headStyle.Render(pad("NODE", 28)+pad("ROLE", 9)+pad("STATUS", 10)+"VERSION") + "\n")
	if len(m.state.nodes) == 0 {
		b.WriteString(dimStyle.Render("    (no nodes)") + "\n")
	}
	for _, n := range m.state.nodes {
		plain := pad(n.Name, 28) + pad(n.Role, 9) + pad(n.Status, 10) + n.Version
		if m.selected(gi) {
			b.WriteString(selStyle.Render("❯ "+plain) + "\n")
		} else {
			st := okStyle.Render(pad(n.Status, 10))
			if n.Status != "Ready" {
				st = warnStyle.Render(pad(n.Status, 10))
			}
			role := pad(n.Role, 9)
			if n.Role == "server" {
				role = titleStyle.Render(role)
			}
			b.WriteString("  " + pad(n.Name, 28) + role + st + dimStyle.Render(n.Version) + "\n")
		}
		gi++
	}
	b.WriteString("\n")

	// pods
	b.WriteString("  " + headStyle.Render(pad("POD", 34)+pad("READY", 7)+pad("STATUS", 10)+pad("RESTARTS", 10)+"NODE") + "\n")
	if len(m.state.pods) == 0 {
		b.WriteString(dimStyle.Render("    (no pods in namespace "+ns+")") + "\n")
	}
	for _, p := range m.state.pods {
		plain := pad(trunc(p.Name, 33), 34) + pad(p.Ready, 7) + pad(p.Status, 10) + pad(fmt.Sprintf("%d", p.Restarts), 10) + p.Node
		if m.selected(gi) {
			b.WriteString(selStyle.Render("❯ "+plain) + "\n")
		} else {
			st := pad(p.Status, 10)
			if p.Status == "Running" {
				st = okStyle.Render(st)
			} else {
				st = warnStyle.Render(st)
			}
			rs := pad(fmt.Sprintf("%d", p.Restarts), 10)
			if p.Restarts > 0 {
				rs = warnStyle.Render(rs)
			}
			b.WriteString("  " + pad(trunc(p.Name, 33), 34) + pad(p.Ready, 7) + st + rs + dimStyle.Render(p.Node) + "\n")
		}
		gi++
	}
	b.WriteString("\n")

	// jobs
	b.WriteString("  " + headStyle.Render(pad("JOB", 34)+pad("SOURCE", 9)+pad("CONC", 6)+pad("EGRESS", 8)+"UPDATED") + "\n")
	if m.state.jobsErr != "" {
		b.WriteString(dimStyle.Render("    "+m.state.jobsErr) + "\n")
	} else if len(m.state.jobs) == 0 {
		b.WriteString(dimStyle.Render("    (no jobs defined)") + "\n")
	}
	for _, j := range m.state.jobs {
		plain := pad(trunc(j.name, 33), 34) + pad(j.source, 9) + pad(j.conc, 6) + pad(j.egress, 8) + j.updated
		if m.selected(gi) {
			b.WriteString(selStyle.Render("❯ "+plain) + "\n")
		} else {
			eg := okStyle.Render(pad(j.egress, 8))
			if j.egress == "yes" {
				eg = warnStyle.Render(pad(j.egress, 8))
			}
			b.WriteString("  " + pad(trunc(j.name, 33), 34) + pad(j.source, 9) + pad(j.conc, 6) + eg + dimStyle.Render(j.updated) + "\n")
		}
		gi++
	}
	return b.String()
}

func (m model) selected(i int) bool { return m.view == viewDash && i == m.cursor }

func (m model) searchView() string {
	var b strings.Builder
	b.WriteString(titleStyle.Render(" search ") + dimStyle.Render("  decisions · context · activity (the knowledge graph)") + "\n\n")
	b.WriteString("  " + keyStyle.Render("/ ") + m.searchQuery + dimStyle.Render("▌") + "\n\n")
	b.WriteString(boxStyle.Render(m.logs.View()) + "\n")
	return b.String()
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

// settingsView renders the per-job checkbox settings. Space toggles the highlighted setting.
func (m model) settingsView() string {
	var b strings.Builder
	b.WriteString(titleStyle.Render(" settings ") + dimStyle.Render("  job: "+m.setJob.name) + "\n\n")
	items := jobSettings()
	for i, s := range items {
		box := "[ ]"
		if s.get(m.setJob) {
			box = "[x]"
		}
		line := box + "  " + s.label
		if i == m.setCursor {
			b.WriteString(selStyle.Render("❯ "+line) + "\n")
		} else {
			mark := dimStyle.Render(box)
			if s.get(m.setJob) {
				mark = okStyle.Render(box)
			}
			b.WriteString("  " + mark + "  " + s.label + "\n")
		}
		b.WriteString("       " + dimStyle.Render(s.desc) + "\n\n")
	}

	// Timeout row — adjustable numeric value (not a checkbox); last in the list.
	to := strings.TrimSpace(m.setJob.timeout)
	if to == "" {
		to = "300"
	}
	val := okStyle.Render("‹ " + to + "s ›")
	tline := "⏱  per-container timeout   " + val
	if m.setCursor == len(items) {
		b.WriteString(selStyle.Render("❯ "+tline) + "\n")
	} else {
		b.WriteString("  " + tline + "\n")
	}
	b.WriteString("       " + dimStyle.Render("Max wall-clock per container before it's killed and the shard fails. ←/→ to adjust (30s–3600s).") + "\n\n")

	b.WriteString("  " + dimStyle.Render("changes persist immediately and apply on the job's next run.") + "\n")
	return b.String()
}

func (m model) confirmView() string {
	var b strings.Builder
	b.WriteString(errStyle.Render(" ⚠  Confirm destructive action ") + "\n\n")
	b.WriteString("  " + warnStyle.Render(m.confirmText) + "\n\n")
	b.WriteString("  " + keyStyle.Render("[y]") + dimStyle.Render(" yes, kill it     ") +
		keyStyle.Render("[n]") + dimStyle.Render(" cancel") + "\n")
	return b.String()
}

func (m model) menuView() string {
	var b strings.Builder
	b.WriteString(titleStyle.Render(" "+m.menuTitle+" ") + "\n\n")
	for i, it := range m.menu {
		if i == m.menuCursor {
			b.WriteString(selStyle.Render("❯ "+pad(it.label, 34)) + "  " + dimStyle.Render(it.desc) + "\n")
		} else {
			b.WriteString("  " + pad(it.label, 34) + "  " + dimStyle.Render(it.desc) + "\n")
		}
	}
	return b.String()
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

// renderMarkdown renders text as Markdown (word-wrapped, .md-styled) for the search results pane.
// Falls back to the raw text if the renderer can't initialise.
func renderMarkdown(s string, width int) string {
	if width < 20 {
		width = 80
	}
	r, err := glamour.NewTermRenderer(glamour.WithAutoStyle(), glamour.WithWordWrap(width))
	if err != nil {
		return s
	}
	out, err := r.Render(s)
	if err != nil {
		return s
	}
	return strings.TrimRight(out, "\n")
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

func (m model) footer() string {
	k := func(key, label string) string { return keyStyle.Render("["+key+"]") + dimStyle.Render(label) }
	var keys []string
	switch m.view {
	case viewDash:
		keys = []string{k("↑↓", "nav"), k("⏎", "logs/runs"), k("R", "run job"), k("s", "settings"), k("x", "kill job"),
			k("/", "search"), k("g", "metrics"), k("m", "mcp"), k("p", "portal"), k("$", "subscribe"),
			k("a", "add worker"), k("c", "theme"), k("r", "refresh"), k("q", "quit")}
	case viewRuns:
		keys = []string{k("↑↓", "nav"), k("⏎", "open run"), k("r", "refresh"), k("b", "back"), k("q", "quit")}
	case viewRunDetail:
		keys = []string{k("↑↓", "scroll"), k("b", "back"), k("q", "quit")}
	case viewSettings:
		keys = []string{k("↑↓", "nav"), k("space", "toggle"), k("←→", "timeout"), k("b", "back"), k("q", "quit")}
	case viewConfirm:
		keys = []string{k("y", "confirm"), k("n", "cancel"), k("q", "quit")}
	case viewMcp:
		keys = []string{k("↑↓", "nav"), k("⏎", "detail"), k("r", "refresh"), k("b", "back"), k("q", "quit")}
	case viewMetrics:
		keys = []string{k("r", "refresh"), k("b", "back"), k("q", "quit")}
	case viewSearch:
		keys = []string{k("type", "query"), k("⏎", "search"), k("esc", "back")}
	case viewMenu:
		keys = []string{k("↑↓", "nav"), k("⏎", "select"), k("b", "back"), k("q", "quit")}
	default:
		keys = []string{k("r", "refresh"), k("b", "back"), k("q", "quit")}
	}
	return "  " + strings.Join(keys, "  ")
}

// ── helpers ─────────────────────────────────────────────────────────────────────────────────────
func resolveKubeconfig() string {
	cmd := exec.Command("k3d", "kubeconfig", "write", cluster)
	cmd.Env = childEnv()
	out, err := cmd.Output()
	if err != nil {
		home, _ := os.UserHomeDir()
		return filepath.Join(home, ".kube", "config")
	}
	return strings.TrimSpace(string(out))
}

// findPctl locates the pctl engine: $PCTL_BIN, sibling of this binary, repo deploy/, or PATH.
func findPctl() string {
	if p := os.Getenv("PCTL_BIN"); p != "" {
		return p
	}
	if exe, err := os.Executable(); err == nil {
		cand := filepath.Join(filepath.Dir(exe), "pctl")
		if fileExists(cand) {
			return cand
		}
	}
	for _, c := range []string{"deploy/pctl", "./pctl", "../pctl"} {
		if fileExists(c) {
			if abs, err := filepath.Abs(c); err == nil {
				return abs
			}
		}
	}
	return "pctl"
}

func fileExists(p string) bool { fi, err := os.Stat(p); return err == nil && !fi.IsDir() }
func pad(s string, n int) string {
	if len(s) >= n {
		return s + " "
	}
	return s + strings.Repeat(" ", n-len(s))
}
func trunc(s string, n int) string {
	if len(s) <= n {
		return s
	}
	return s[:n-1] + "…"
}
func abs(a int) int {
	if a < 0 {
		return -a
	}
	return a
}
func sgn(a int) int {
	if a > 0 {
		return 1
	}
	if a < 0 {
		return -1
	}
	return 0
}
func max(a, b int) int {
	if a > b {
		return a
	}
	return b
}

func main() {
	if len(os.Args) > 1 && os.Args[1] == "url" {
		m := initialModel()
		key := m.portalSigningKey()
		target := portalURL()
		if key != "" {
			target += "auth/portal?token=" + neturl.QueryEscape(mintPortalToken(key, time.Now()))
		}
		fmt.Printf("kubeconfig=%s\nsigningKey.len=%d\ntarget=%s\n", m.kubeconfig, len(key), target)
		return
	}
	// If a mesh is configured (PCTL_MESH_CONTROL + PCTL_MESH_AUTHKEY) and this isn't the host running
	// the cluster, join the tailnet so a remote operator can see jobs/logs over the mesh; disconnect on
	// exit. Ephemeral keys mean the node auto-deregisters.
	if joinedMesh() {
		defer leaveMesh()
	}
	p := tea.NewProgram(initialModel(), tea.WithAltScreen())
	if _, err := p.Run(); err != nil {
		fmt.Fprintln(os.Stderr, "pctl-tui error:", err)
		os.Exit(1)
	}
}

// joinedMesh brings this machine onto the self-hosted mesh (Headscale/Tailscale) for the session.
// No-op (returns false) when no mesh is configured, tailscale is absent, or we're the cluster host.
func joinedMesh() bool {
	ctrl, key := os.Getenv("PCTL_MESH_CONTROL"), meshAuthKey()
	if ctrl == "" || key == "" || !commandExists("tailscale") || isClusterHost() {
		return false
	}
	ctx, cancel := context.WithTimeout(context.Background(), 30*time.Second)
	defer cancel()
	cmd := exec.CommandContext(ctx, "tailscale", "up",
		"--login-server="+ctrl, "--authkey="+key, "--hostname=pctl-tui", "--accept-routes")
	cmd.Env = childEnv()
	if err := cmd.Run(); err != nil {
		fmt.Fprintln(os.Stderr, "mesh: could not join ("+err.Error()+") — continuing without it")
		return false
	}
	fmt.Fprintln(os.Stderr, "mesh: joined "+ctrl)
	return true
}

func leaveMesh() {
	cmd := exec.Command("tailscale", "down")
	cmd.Env = childEnv()
	_ = cmd.Run()
}

// isClusterHost reports whether this machine hosts the cluster (a local k3d cluster or a running k3s
// server) — in which case it's already reachable and shouldn't auto-join the mesh as a viewer.
func isClusterHost() bool {
	out, err := exec.Command("k3d", "cluster", "list", "-o", "json").Output()
	if err == nil && strings.Contains(string(out), "\"name\":\""+cluster+"\"") {
		return true
	}
	return false
}
