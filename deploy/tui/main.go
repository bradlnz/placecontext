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
		if st.dbUp {
			st.jobs, st.jobsErr = mc.queryJobs(ctx)
		}
		return stateMsg(st)
	}
}

func (m model) fetchLogs() tea.Cmd {
	mc := m
	return func() tea.Msg {
		ctx, cancel := context.WithTimeout(context.Background(), 8*time.Second)
		defer cancel()
		b, err := mc.kubectl(ctx, "-n", ns, "logs", "-l", "app=placecontext", "--tail=200", "--prefix")
		if err != nil {
			return logsMsg{"all host pods", "could not fetch logs: " + err.Error()}
		}
		return logsMsg{"all host pods", string(b)}
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
			q := `SELECT "StartedAt","Status","FinishedAt" FROM job_runs WHERE "JobId" IN ` +
				`(SELECT "Id" FROM jobs WHERE "Name"='` + esc + `') ORDER BY "StartedAt" DESC LIMIT 50`
			b, err := mc.kubectl(ctx, "-n", ns, "exec", "deploy/placecontext-db", "--",
				"psql", "-U", "postgres", "-d", "placecontext", "-c", q)
			if err != nil {
				return logsMsg{title, "could not fetch job runs: " + err.Error()}
			}
			body := string(b)
			if strings.TrimSpace(body) == "" {
				body = "(no runs yet for this job)"
			}
			return logsMsg{title, "recent runs:\n\n" + body}
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
					return m, m.fetchMcpDetail(m.mcp[m.mcpCursor])
				}
			case "r":
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
				return m, m.fetchLogsFor(it)
			}
		case "a":
			if !m.busy {
				m.menu, m.menuTitle, m.menuCursor, m.view = addNodeMenu(), "Add node", 0, viewMenu
			}
			return m, nil
		case "g":
			if !m.busy {
				m.view = viewMetrics
				return m, tea.Batch(m.fetchMetrics(), metricsTick())
			}
			return m, nil
		case "p":
			return m, m.openPortal()
		case "m":
			if m.view == viewDash && !m.busy {
				m.view, m.mcpCursor = viewMcp, 0
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
				return m, m.fetchLogsFor(m.sel[m.cursor])
			}
			if m.view == viewMcp {
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
		return m, tea.Batch(m.fetchMetrics(), metricsTick())

	case metricsMsg:
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
		m.logTitle = msg.title
		m.logs.SetContent(msg.body)
		m.logs.GotoTop()
		return m, nil

	case mcpMsg:
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

func (m model) dashboard() string {
	// Side-by-side: cluster on the left, selectable node/pod/job list on the right. This keeps the
	// page short (so the banner/footer don't scroll off as more items are added).
	rows := m.h - 11 // height available below the banner/health, above the footer
	if rows < 8 {
		rows = 8
	} else if rows > 26 {
		rows = 26
	}
	leftW := m.w * 3 / 5
	if leftW < 36 {
		leftW = 36
	}
	if leftW > m.w-24 {
		leftW = max(24, m.w-24)
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

func (m model) footer() string {
	k := func(key, label string) string { return keyStyle.Render("["+key+"]") + dimStyle.Render(label) }
	var keys []string
	switch m.view {
	case viewDash:
		keys = []string{k("↑↓", "nav"), k("⏎", "logs"), k("x", "kill"), k("g", "metrics"), k("m", "mcp"),
			k("p", "portal"), k("a", "add node"), k("space", "spin"), k("+/-", "zoom"),
			k("u", "up"), k("d", "down"), k("r", "refresh"), k("q", "quit")}
	case viewConfirm:
		keys = []string{k("y", "confirm"), k("n", "cancel"), k("q", "quit")}
	case viewMcp:
		keys = []string{k("↑↓", "nav"), k("⏎", "detail"), k("r", "refresh"), k("b", "back"), k("q", "quit")}
	case viewMetrics:
		keys = []string{k("r", "refresh"), k("b", "back"), k("q", "quit")}
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
	p := tea.NewProgram(initialModel(), tea.WithAltScreen())
	if _, err := p.Run(); err != nil {
		fmt.Fprintln(os.Stderr, "pctl-tui error:", err)
		os.Exit(1)
	}
}
