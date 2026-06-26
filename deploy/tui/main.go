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
	"context"
	"encoding/json"
	"fmt"
	neturl "net/url"
	"os"
	"os/exec"
	"path/filepath"
	"strings"
	"time"

	"github.com/charmbracelet/bubbles/spinner"
	"github.com/charmbracelet/bubbles/viewport"
	tea "github.com/charmbracelet/bubbletea"
	"github.com/charmbracelet/glamour"
	"github.com/charmbracelet/lipgloss"
)

// PlaceContext ASCII banner (small figlet).
const banner = `  ___ _              ___         _           _
 | _ \ |__ _ __ ___ / __|___ _ _| |_ _____ _| |_
 |  _/ / _' / _/ -_) (__/ _ \ ' \  _/ -_) \ /  _|
 |_| |_\__,_\__\___|\___\___/_||_\__\___/_\_\\__|`

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
	return cmd.Output()
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
		if it.kind == "job" {
			title := "job/" + it.name
			esc := strings.ReplaceAll(it.name, "'", "''")
			// recent runs (status table) + the latest run's actual output (map shard + reduce results)
			runsQ := `SELECT "StartedAt","Status","FinishedAt" FROM job_runs WHERE "JobId" IN ` +
				`(SELECT "Id" FROM jobs WHERE "Name"='` + esc + `') ORDER BY "StartedAt" DESC LIMIT 20`
			runs, err := mc.kubectl(ctx, "-n", ns, "exec", "deploy/placecontext-db", "--",
				"psql", "-U", "postgres", "-d", "placecontext", "-c", runsQ)
			if err != nil {
				return logsMsg{title, "could not fetch job runs: " + err.Error()}
			}
			outQ := `SELECT coalesce("ReduceResultJson", "ShardResultsJson") FROM job_runs WHERE "JobId" IN ` +
				`(SELECT "Id" FROM jobs WHERE "Name"='` + esc + `') ORDER BY "StartedAt" DESC LIMIT 1`
			out, _ := mc.kubectl(ctx, "-n", ns, "exec", "deploy/placecontext-db", "--",
				"psql", "-U", "postgres", "-d", "placecontext", "-At", "-c", outQ)
			runsTxt := strings.TrimSpace(string(runs))
			if runsTxt == "" {
				runsTxt = "(no runs yet for this job)"
			}
			outTxt := strings.TrimSpace(string(out))
			if outTxt == "" {
				outTxt = "(no output recorded for the latest run)"
			}
			return logsMsg{title, "recent runs:\n\n" + runsTxt + "\n\nlatest run output:\n\n" + outTxt}
		}
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

type jobRow struct{ name, source, conc, egress, updated string }

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
	const q = `SELECT "Name", "MapSourceKind", "ConcurrencyLimit", "AllowNetworkEgress", "UpdatedAt" ` +
		`FROM jobs ORDER BY "UpdatedAt" DESC LIMIT 100`
	b, err := m.kubectl(ctx, "-n", ns, "exec", "deploy/placecontext-db", "--",
		"psql", "-U", "postgres", "-d", "placecontext", "-At", "-F", "\t", "-c", q)
	if err != nil {
		return nil, "could not query jobs: " + err.Error()
	}
	var rows []jobRow
	for _, ln := range strings.Split(strings.TrimSpace(string(b)), "\n") {
		if ln == "" {
			continue
		}
		f := strings.Split(ln, "\t")
		for len(f) < 5 {
			f = append(f, "")
		}
		eg := "no"
		if f[3] == "t" {
			eg = "yes"
		}
		upd := f[4]
		if len(upd) > 19 {
			upd = upd[:19]
		}
		rows = append(rows, jobRow{f[0], f[1], f[2], eg, upd})
	}
	return rows, ""
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
			// confirm and the logs detail return to wherever they were opened from; everything else
			// returns to the dashboard.
			if m.view == viewConfirm || m.view == viewLogs {
				m.view = m.prevView
			} else {
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
				return m, tea.Batch(m.sp.Tick, m.runAction(m.confirmVerb, m.confirmArgs...))
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
					return m, tea.Batch(m.sp.Tick, m.runAction(it.verb, it.args...))
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
				m.prevView = viewDash
				m.view = viewLogs
				m.logs.SetContent("")
				m.loading = true
				return m, m.fetchLogsFor(it)
			}
		case "a":
			// One keypress adds a worker computer to the cluster — no jargon, no choices.
			if !m.busy {
				m.busy, m.busyVerb, m.view = true, "adding a worker", viewAction
				m.out.SetContent("")
				return m, tea.Batch(m.sp.Tick, m.runAction("adding a worker", "dev", "add-node", "--role", "agent"))
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
			if m.view == viewDash && !m.busy && m.cursor < len(m.sel) {
				m.prevView = viewDash
				m.armKill(m.sel[m.cursor])
				m.view = viewConfirm
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
		case "u":
			if !m.busy {
				m.busy, m.busyVerb, m.view = true, "dev up", viewAction
				m.out.SetContent("")
				return m, tea.Batch(m.sp.Tick, m.runAction("dev up", "dev", "up"))
			}
		case "d":
			if !m.busy {
				m.busy, m.busyVerb, m.view = true, "dev down", viewAction
				m.out.SetContent("")
				return m, tea.Batch(m.sp.Tick, m.runAction("dev down", "dev", "down"))
			}
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

	case metricsTickMsg:
		if m.view != viewMetrics {
			return m, nil // stop sampling when the metrics view isn't shown
		}
		m.loading = true
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
	case viewLogs:
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
	var b strings.Builder
	b.WriteString(bannerStyle.Render(banner) + "\n")
	b.WriteString(dimStyle.Render("  hosted multi-tenant context · MCP + portal") + "\n\n")
	b.WriteString(m.healthLine() + "\n")
	if a := m.alerts(); a != "" {
		b.WriteString(a)
	}
	if m.loading {
		b.WriteString("  " + boxStyle.Render(m.sp.View()+dimStyle.Render(" loading…")) + "\n")
	} else if m.flash != "" {
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
		keys = []string{k("↑↓", "nav"), k("⏎", "logs"), k("/", "search"), k("x", "kill"), k("g", "metrics"),
			k("m", "mcp"), k("p", "portal"), k("$", "subscribe"), k("a", "add worker"), k("c", "theme"),
			k("u", "up"), k("d", "down"), k("r", "refresh"), k("q", "quit")}
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
