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
	"crypto/hmac"
	"crypto/rand"
	"crypto/sha256"
	"encoding/base64"
	"encoding/hex"
	"encoding/json"
	"fmt"
	"math"
	neturl "net/url"
	"os"
	"os/exec"
	"path/filepath"
	"sort"
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
	brainStyle  = lipgloss.NewStyle().Foreground(lipgloss.Color("141")).Bold(true)
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
	viewBrain
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

	// 3D brain view
	brainPts  []pt3
	brainAngX float64
	brainAngY float64
	brainZoom float64
	brainSpin bool

	// destructive-action confirmation
	prevView    view
	confirmText string
	confirmVerb string
	confirmArgs []string

	// 3D cluster view (the main page default)
	dash3D bool
	clAngX float64 // viewing tilt (user)
	clAngY float64 // viewing yaw (user)
	clZoom float64
	clSpin bool
	orbit  float64 // orbital animation phase (advances while clSpin)

	// MCP tool-call log
	mcp    []mcpRow
	mcpErr string
}

type pt3 struct{ x, y, z float64 }

// ── messages ──────────────────────────────────────────────────────────────────────────────────
type tickMsg time.Time
type stateMsg clusterState
type logsMsg struct{ title, body string }
type actionDoneMsg struct {
	verb, output string
	err          error
}

type flashMsg string
type brainTickMsg time.Time
type clusterTickMsg time.Time

func brainTick() tea.Cmd {
	return tea.Tick(80*time.Millisecond, func(t time.Time) tea.Msg { return brainTickMsg(t) })
}

func clusterTick() tea.Cmd {
	return tea.Tick(90*time.Millisecond, func(t time.Time) tea.Msg { return clusterTickMsg(t) })
}

// selItem is one keyboard-selectable row in the dashboard (a node or a pod).
type selItem struct{ kind, name, node string } // kind: "node" | "pod"

func portalURL() string {
	port := os.Getenv("PCTL_PORT")
	if port == "" {
		port = "7700"
	}
	return "http://localhost:" + port + "/"
}

// openPortal opens the portal in the default browser (best-effort, cross-platform). The portal has no
// password login: we read the shared signing key from the cluster secret, mint a short-lived token, and
// hand it to /auth/portal so the operator is signed in automatically. With no key reachable (e.g. local
// `./run.sh` with no cluster) we open the bare URL — the host auto-signs-in in Development.
func (m model) openPortal() tea.Cmd {
	target := portalURL()
	if key := m.portalSigningKey(); key != "" {
		target += "auth/portal?token=" + neturl.QueryEscape(mintPortalToken(key, time.Now()))
	}
	return func() tea.Msg {
		var bin string
		var args []string
		switch {
		case commandExists("xdg-open"):
			bin, args = "xdg-open", []string{target}
		case commandExists("open"):
			bin, args = "open", []string{target}
		default:
			return flashMsg("portal: " + portalURL() + " (no browser opener found)")
		}
		cmd := exec.Command(bin, args...)
		cmd.Env = childEnv()
		if err := cmd.Start(); err != nil {
			return flashMsg("portal: " + portalURL() + " (open failed: " + err.Error() + ")")
		}
		return flashMsg("opened " + portalURL() + " in your browser")
	}
}

// portalSigningKey reads the shared HMAC key from the placecontext-portal cluster secret. Returns "" if
// it can't be read (secret absent, or no cluster), so the caller can fall back to a token-less open.
func (m model) portalSigningKey() string {
	ctx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
	defer cancel()
	out, err := m.kubectl(ctx, "-n", ns, "get", "secret", "placecontext-portal",
		"-o", "jsonpath={.data.signing-key}")
	if err != nil || len(out) == 0 {
		return ""
	}
	raw, err := base64.StdEncoding.DecodeString(strings.TrimSpace(string(out)))
	if err != nil {
		return ""
	}
	return string(raw)
}

// mintPortalToken builds the 60-second HMAC token the host validates (see Host/Auth/PortalToken.cs):
//
//	payload = "<expUnix>.<nonceHex>"
//	token   = payload + "." + base64url-unpadded( HMAC_SHA256(key, payload) )
func mintPortalToken(key string, now time.Time) string {
	nonce := make([]byte, 8)
	_, _ = rand.Read(nonce)
	payload := fmt.Sprintf("%d.%s", now.Add(60*time.Second).Unix(), hex.EncodeToString(nonce))
	mac := hmac.New(sha256.New, []byte(key))
	mac.Write([]byte(payload))
	return payload + "." + base64.RawURLEncoding.EncodeToString(mac.Sum(nil))
}

func commandExists(name string) bool { _, err := exec.LookPath(name); return err == nil }

func tick() tea.Cmd {
	return tea.Tick(3*time.Second, func(t time.Time) tea.Msg { return tickMsg(t) })
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

type mcpRow struct{ at, tool, dir, status, dur string }
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
		const q = `SELECT "At", "Tool", "Direction", "Status", "DurationMs" ` +
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
			for len(f) < 5 {
				f = append(f, "")
			}
			at := f[0]
			if len(at) > 19 {
				at = at[:19]
			}
			rows = append(rows, mcpRow{at, f[1], f[2], f[3], f[4]})
		}
		return mcpMsg{rows, ""}
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
		brainPts:   generateBrain(),
		brainZoom:  1.0,
		brainSpin:  true,
		dash3D:     true,
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
			if m.view == viewConfirm {
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

		// 3D brain — steer with arrows, +/- zoom, space toggles auto-spin
		if m.view == viewBrain {
			switch key {
			case "left", "h":
				m.brainAngY -= 0.2
			case "right", "l":
				m.brainAngY += 0.2
			case "up", "k":
				m.brainAngX -= 0.2
			case "down", "j":
				m.brainAngX += 0.2
			case "+", "=":
				m.brainZoom *= 1.1
			case "-", "_":
				m.brainZoom /= 1.1
			case " ":
				m.brainSpin = !m.brainSpin
			}
			return m, nil
		}

		switch key {
		case "up", "k":
			if m.view == viewDash {
				if m.dash3D {
					m.clAngX -= 0.15
				} else if m.cursor > 0 {
					m.cursor--
				}
			}
		case "down", "j":
			if m.view == viewDash {
				if m.dash3D {
					m.clAngX += 0.15
				} else if m.cursor < len(m.sel)-1 {
					m.cursor++
				}
			}
		case "left":
			if m.view == viewDash && m.dash3D {
				m.clAngY -= 0.18
			}
		case "right":
			if m.view == viewDash && m.dash3D {
				m.clAngY += 0.18
			}
		case "+", "=":
			if m.view == viewDash && m.dash3D {
				m.clZoom *= 1.1
			}
		case "-", "_":
			if m.view == viewDash && m.dash3D {
				m.clZoom /= 1.1
			}
		case " ":
			if m.view == viewDash && m.dash3D {
				m.clSpin = !m.clSpin
			}
		case "v":
			if m.view == viewDash && !m.busy {
				m.dash3D = !m.dash3D
				if m.dash3D {
					return m, clusterTick()
				}
			}
			return m, nil
		case "enter":
			if m.view == viewDash && !m.dash3D && m.cursor < len(m.sel) {
				it := m.sel[m.cursor]
				m.view = viewLogs
				m.logs.SetContent("")
				return m, m.fetchLogsFor(it)
			}
		case "a":
			if !m.busy {
				m.menu, m.menuTitle, m.menuCursor, m.view = addNodeMenu(), "Add node", 0, viewMenu
			}
			return m, nil
		case "z":
			if !m.busy {
				m.view = viewBrain
				return m, brainTick()
			}
			return m, nil
		case "p":
			return m, m.openPortal()
		case "m":
			if m.view == viewDash && !m.busy {
				m.view = viewMcp
				return m, m.fetchMcp()
			}
			return m, nil
		case "x":
			if m.view == viewDash && !m.dash3D && !m.busy && m.cursor < len(m.sel) {
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

	case brainTickMsg:
		if m.view != viewBrain {
			return m, nil // leaving the brain view stops the animation loop
		}
		if m.brainSpin {
			m.brainAngY += 0.06
		}
		return m, brainTick()

	case clusterTickMsg:
		if m.view != viewDash || !m.dash3D {
			return m, nil // only animate while the 3D dashboard is showing
		}
		if m.clSpin {
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
	case viewBrain:
		b.WriteString(m.brainView())
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
	if m.dash3D {
		return m.cluster3DView()
	}
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

func (m model) selected(i int) bool { return m.view == viewDash && !m.dash3D && i == m.cursor }

// scenePalette maps a colour id → style.
// 0 server, 1 worker, 2 pod-ok, 3 pod-pending, 4 db, 5 link.
var scenePalette = []lipgloss.Style{
	lipgloss.NewStyle().Foreground(lipgloss.Color("44")).Bold(true),  // server (teal)
	lipgloss.NewStyle().Foreground(lipgloss.Color("39")).Bold(true),  // worker (blue)
	lipgloss.NewStyle().Foreground(lipgloss.Color("42")).Bold(true),  // pod ok (green)
	lipgloss.NewStyle().Foreground(lipgloss.Color("220")).Bold(true), // pod pending (yellow)
	lipgloss.NewStyle().Foreground(lipgloss.Color("141")).Bold(true), // db (magenta)
	lipgloss.NewStyle().Foreground(lipgloss.Color("238")),            // link (faint)
}

// canvas is a coloured character grid for drawing the topology (lines + markers + labels).
type canvas struct {
	w, h int
	ch   []rune
	col  []int
}

func newCanvas(w, h int) *canvas {
	c := &canvas{w: w, h: h, ch: make([]rune, w*h), col: make([]int, w*h)}
	for i := range c.ch {
		c.ch[i] = ' '
		c.col[i] = -1
	}
	return c
}
func (c *canvas) set(x, y int, r rune, color int) {
	if x >= 0 && x < c.w && y >= 0 && y < c.h {
		c.ch[y*c.w+x] = r
		c.col[y*c.w+x] = color
	}
}
func (c *canvas) text(x, y int, s string, color int) {
	for i, r := range []rune(s) {
		c.set(x+i, y, r, color)
	}
}

// box draws a labelled rectangle centred on (cxp,cyp) — a little "computer" icon for a
// cluster item. The interior is blanked so connecting lines don't bleed through.
func (c *canvas) box(cxp, cyp int, label string, color int) {
	r := []rune(label)
	iw := len(r)
	w := iw + 2
	x0 := cxp - w/2
	y0 := cyp - 1
	c.set(x0, y0, '┌', color)
	c.set(x0+w-1, y0, '┐', color)
	c.set(x0, y0+2, '└', color)
	c.set(x0+w-1, y0+2, '┘', color)
	for i := 1; i < w-1; i++ {
		c.set(x0+i, y0, '─', color)
		c.set(x0+i, y0+2, '─', color)
	}
	c.set(x0, y0+1, '│', color)
	c.set(x0+w-1, y0+1, '│', color)
	c.text(x0+1, y0+1, label, color)
}

// line draws an edge with Bresenham, choosing a glyph by slope. It won't overwrite a
// non-link cell, so markers/labels drawn earlier stay legible.
func (c *canvas) line(x0, y0, x1, y1, color int) {
	dx, dy := x1-x0, y1-y0
	ax, ay := abs(dx), abs(dy)
	glyph := '·'
	switch {
	case ax > ay*2:
		glyph = '─'
	case ay > ax*2:
		glyph = '│'
	case dx*dy < 0:
		glyph = '╱'
	default:
		glyph = '╲'
	}
	sx, sy := sgn(dx), sgn(dy)
	err := ax - ay
	x, y := x0, y0
	for {
		if x >= 0 && x < c.w && y >= 0 && y < c.h && c.col[y*c.w+x] == -1 {
			c.ch[y*c.w+x] = glyph
			c.col[y*c.w+x] = color
		}
		if x == x1 && y == y1 {
			break
		}
		e2 := 2 * err
		if e2 > -ay {
			err -= ay
			x += sx
		}
		if e2 < ax {
			err += ax
			y += sy
		}
	}
}

func (c *canvas) String() string {
	var b strings.Builder
	for y := 0; y < c.h; y++ {
		x := 0
		for x < c.w {
			col := c.col[y*c.w+x]
			j := x
			var run []rune
			for j < c.w && c.col[y*c.w+j] == col {
				run = append(run, c.ch[y*c.w+j])
				j++
			}
			if col < 0 {
				b.WriteString(string(run))
			} else {
				b.WriteString(scenePalette[col].Render(string(run)))
			}
			x = j
		}
		b.WriteByte('\n')
	}
	return b.String()
}

func center(s string, width int) string {
	r := []rune(s)
	if len(r) >= width {
		return string(r[:width])
	}
	left := (width - len(r)) / 2
	return strings.Repeat(" ", left) + s + strings.Repeat(" ", width-len(r)-left)
}

// cylinder draws a 3D cylinder/globe centred on (cxp,cyp) — the control-plane hub the
// satellites orbit. The "═" bands read as a globe's latitudes; the elliptical top and
// bottom give it depth.
func (c *canvas) cylinder(cxp, cyp int, label string, color int) {
	iw := len([]rune(label))
	if iw < 6 {
		iw = 6
	}
	w := iw + 2
	x0 := cxp - w/2
	y0 := cyp - 3
	dash := strings.Repeat("─", iw)
	band := strings.Repeat("═", iw)
	rows := []string{
		" " + dash + " ",
		"╭" + band + "╮",
		"│" + center(label, iw) + "│",
		"│" + strings.Repeat(" ", iw) + "│",
		"╰" + band + "╯",
		" " + dash + " ",
	}
	for dy, row := range rows {
		c.text(x0, y0+dy, row, color)
	}
}

// trimToBox returns the point on the border of a box (centre cx,cy, half-extents hw,hh)
// in the direction of (tx,ty) — so link lines start/end at the box edge, not its centre.
func trimToBox(cx, cy, hw, hh, tx, ty int) (int, int) {
	dx, dy := float64(tx-cx), float64(ty-cy)
	if dx == 0 && dy == 0 {
		return cx, cy
	}
	s := math.Inf(1)
	if dx != 0 {
		s = math.Min(s, float64(hw)/math.Abs(dx))
	}
	if dy != 0 {
		s = math.Min(s, float64(hh)/math.Abs(dy))
	}
	return cx + int(dx*s), cy + int(dy*s)
}

func shortName(s string) string {
	s = strings.TrimPrefix(s, "k3d-"+cluster+"-")
	s = strings.TrimPrefix(s, "placecontext-")
	return s
}

// podLabel keeps pod boxes compact: "db" for the database, otherwise the unique
// suffix (the bit after the last hyphen) so replicas stay distinguishable.
func podLabel(name string) string {
	if strings.HasPrefix(name, "placecontext-db") {
		return "db"
	}
	if i := strings.LastIndex(name, "-"); i >= 0 && i < len(name)-1 {
		return name[i+1:]
	}
	return shortName(name)
}

// cluster3DView renders the live cluster as an animated 3D system-topology graph: the
// control-plane and workers laid out in a rotating ring, each pod linked to its node by
// a line, every entity labelled with its (shortened) name.
func (m model) cluster3DView() string {
	if !m.state.reach {
		return "  " + warnStyle.Render("● no cluster") + dimStyle.Render("   press [u] to bring it up, or [v] for the list view") + "\n"
	}
	w, h := m.w-2, m.h-13
	if w < 30 {
		w = 80
	}
	if h < 10 {
		h = 20
	}
	cv := newCanvas(w, h)

	type p3 struct{ x, y, z float64 }
	nodePos := map[string]p3{}
	var servers, workers []nodeRow
	for _, n := range m.state.nodes {
		if n.Role == "server" {
			servers = append(servers, n)
		} else {
			workers = append(workers, n)
		}
	}
	orb := m.orbit
	// control-plane hub(s) at the centre
	for i, s := range servers {
		nodePos[s.Name] = p3{0, 0.2 - 0.5*float64(i), 0}
	}
	// workers orbit the hub like satellites (slow, majestic)
	nw := max(1, len(workers))
	const ring = 4.4
	for i, wk := range workers {
		th := 2*math.Pi*float64(i)/float64(nw) + orb*0.6
		nodePos[wk.Name] = p3{ring * math.Cos(th), -0.2, ring * math.Sin(th)}
	}

	// viewing transform: fixed tilt + user clAngX around X, user yaw clAngY around Y
	angX := 0.5 + m.clAngX
	sinX, cosX := math.Sin(angX), math.Cos(angX)
	sinY, cosY := math.Sin(m.clAngY), math.Cos(m.clAngY)
	const dist = 11.0
	cx, cy := float64(w)/2, float64(h)/2
	project := func(p p3) (int, int, float64) {
		x1 := p.x*cosY + p.z*sinY
		z1 := -p.x*sinY + p.z*cosY
		y1 := p.y*cosX - z1*sinX
		z2 := p.y*sinX + z1*cosX
		ooz := 1.0 / (z2 + dist)
		return int(cx + m.clZoom*x1*ooz*float64(w)*0.80),
			int(cy - m.clZoom*y1*ooz*float64(h)*1.6), z2
	}

	// a visible entity, with screen position + box half-extents (for edge-trimmed links)
	type vis struct {
		sx, sy, hw, hh int
		depth          float64
		label          string
		marker         rune
		color          int
		cyl            bool
	}
	scr := map[string][3]int{} // name → sx, sy + radius for link trimming
	var ents []vis

	for _, s := range servers {
		x, y, d := project(nodePos[s.Name])
		lab := shortName(s.Name)
		cw := len([]rune(lab))
		if cw < 6 {
			cw = 6
		}
		cw += 2
		ents = append(ents, vis{x, y, cw / 2, 3, d, lab, '◆', 0, true})
		scr[s.Name] = [3]int{x, y, cw / 2}
	}
	for _, wk := range workers {
		x, y, d := project(nodePos[wk.Name])
		col := 1
		if wk.Status != "Ready" {
			col = 3
		}
		bw := len([]rune("● "+shortName(wk.Name))) + 2
		ents = append(ents, vis{x, y, bw / 2, 1, d, shortName(wk.Name), '●', col, false})
		scr[wk.Name] = [3]int{x, y, bw / 2}
	}

	// pods orbit their worker like moons (tilted ring), each linked to it
	count := map[string]int{}
	for _, p := range m.state.pods {
		count[p.Node]++
	}
	idx := map[string]int{}
	for _, p := range m.state.pods {
		base, ok := nodePos[p.Node]
		if !ok {
			continue
		}
		k := idx[p.Node]
		idx[p.Node]++
		ph := 2*math.Pi*float64(k)/float64(max(1, count[p.Node])) + orb*1.4
		const pr = 1.6
		pp := p3{base.x + pr*math.Cos(ph), base.y + 0.5 + pr*0.45*math.Sin(ph), base.z + pr*math.Sin(ph)}
		px, py, pd := project(pp)
		col, mark := 2, '•'
		if !(p.Ready == "1/1" && p.Status == "Running") {
			col, mark = 3, '◦'
		}
		if strings.HasPrefix(p.Name, "placecontext-db") {
			col, mark = 4, '◆'
		}
		lab := podLabel(p.Name)
		bw := len([]rune(string(mark)+" "+lab)) + 2
		ents = append(ents, vis{px, py, bw / 2, 1, pd, lab, mark, col, false})
		// link pod → its node, trimmed to both borders
		if b, ok := scr[p.Node]; ok {
			ax, ay := trimToBox(b[0], b[1], b[2], 1, px, py)
			bx, by := trimToBox(px, py, bw/2, 1, b[0], b[1])
			cv.line(ax, ay, bx, by, 5)
		}
	}

	// control-plane ↔ worker links (edge to edge)
	for _, s := range servers {
		a := scr[s.Name]
		for _, wk := range workers {
			b := scr[wk.Name]
			ax, ay := trimToBox(a[0], a[1], a[2], 3, b[0], b[1])
			bx, by := trimToBox(b[0], b[1], b[2], 1, a[0], a[1])
			cv.line(ax, ay, bx, by, 5)
		}
	}

	// draw far→near so nearer items win; server hub as a cylinder/globe, others as boxes
	sort.Slice(ents, func(i, j int) bool { return ents[i].depth > ents[j].depth })
	for _, e := range ents {
		if e.cyl {
			cv.cylinder(e.sx, e.sy, e.label, e.color)
		} else {
			cv.box(e.sx, e.sy, string(e.marker)+" "+e.label, e.color)
		}
	}

	spin := "on"
	if !m.clSpin {
		spin = "off"
	}
	var b strings.Builder
	b.WriteString(titleStyle.Render(" cluster ") +
		dimStyle.Render(fmt.Sprintf("  spin:%s  zoom:%.1f×  (←→↑↓ rotate · +/- zoom · space spin · [v] list)", spin, m.clZoom)) + "\n")
	b.WriteString(cv.String())
	leg := func(c int, s string) string { return scenePalette[c].Render(s) }
	b.WriteString(dimStyle.Render("  ") +
		leg(0, "◆ server") + "  " + leg(1, "● worker") + "  " +
		leg(2, "• pod") + "  " + leg(3, "◦ pending") + "  " + leg(4, "◆ db"))
	return b.String()
}

// generateBrain builds a 3D point cloud shaped like a two-lobed brain: two folded
// ellipsoids set side by side with a central fissure. Points are sampled on the
// surface and modulated by sinusoidal "gyri" so the silhouette reads as a brain.
func generateBrain() []pt3 {
	var pts []pt3
	const (
		uSteps = 90
		vSteps = 44
	)
	for i := 0; i < uSteps; i++ {
		theta := math.Pi * float64(i) / float64(uSteps-1) // 0..π (pole to pole)
		for j := 0; j < vSteps; j++ {
			phi := 2 * math.Pi * float64(j) / float64(vSteps) // 0..2π
			// folds: higher frequency around the equator → gyri/sulci
			fold := 0.13*math.Sin(7*phi)*math.Sin(5*theta) + 0.06*math.Cos(9*theta)
			r := 1.0 + fold
			x := r * math.Sin(theta) * math.Cos(phi)
			y := r * math.Cos(theta) * 0.78 // flatten top-to-bottom
			z := r * math.Sin(theta) * math.Sin(phi)
			// two lobes offset along X with a fissure gap in the middle
			for _, side := range []float64{-1, 1} {
				px := x*0.62 + side*0.62
				if side*px < 0.06 { // carve the central fissure
					continue
				}
				pts = append(pts, pt3{px, y, z * 0.95})
			}
		}
	}
	// a short brain-stem so it's unmistakable
	for k := 0; k < 60; k++ {
		t := float64(k) / 59.0
		pts = append(pts, pt3{0, -0.8 - t*0.7, -0.1 + 0.05*math.Sin(t*12)})
	}
	return pts
}

const brainShades = ".,-~:;=!*#$@"

// renderBrain projects the rotated point cloud into an ASCII frame with a depth buffer.
func (m model) renderBrain(w, h int) string {
	if w < 20 {
		w = 70
	}
	if h < 10 {
		h = 22
	}
	zbuf := make([]float64, w*h)
	cbuf := make([]byte, w*h)
	for i := range cbuf {
		cbuf[i] = ' '
	}
	sinA, cosA := math.Sin(m.brainAngX), math.Cos(m.brainAngX)
	sinB, cosB := math.Sin(m.brainAngY), math.Cos(m.brainAngY)

	const dist = 4.0
	scale := m.brainZoom * float64(h) * 0.62
	cx, cy := float64(w)/2, float64(h)/2

	for _, p := range m.brainPts {
		// rotate around Y then X
		x1 := p.x*cosB + p.z*sinB
		z1 := -p.x*sinB + p.z*cosB
		y1 := p.y*cosA - z1*sinA
		z2 := p.y*sinA + z1*cosA
		ooz := 1.0 / (z2 + dist)
		sx := int(cx + scale*x1*ooz*1.9) // ×1.9: terminal cells are ~2:1 tall
		sy := int(cy - scale*y1*ooz)
		if sx < 0 || sx >= w || sy < 0 || sy >= h {
			continue
		}
		idx := sy*w + sx
		if ooz > zbuf[idx] {
			zbuf[idx] = ooz
			lum := (ooz - 0.18) / 0.16 // map depth → brightness
			si := int(lum * float64(len(brainShades)-1))
			if si < 0 {
				si = 0
			}
			if si >= len(brainShades) {
				si = len(brainShades) - 1
			}
			cbuf[idx] = brainShades[si]
		}
	}

	var b strings.Builder
	for y := 0; y < h; y++ {
		b.WriteString(brainStyle.Render(string(cbuf[y*w : (y+1)*w])))
		b.WriteByte('\n')
	}
	return b.String()
}

func (m model) brainView() string {
	var b strings.Builder
	spin := "on"
	if !m.brainSpin {
		spin = "off"
	}
	b.WriteString(titleStyle.Render(" PlaceContext brain — 3D ") +
		dimStyle.Render(fmt.Sprintf("  spin:%s  zoom:%.1f×  (←→↑↓ rotate · +/- zoom · space spin)", spin, m.brainZoom)) + "\n")
	w := m.w - 2
	h := m.h - 12
	b.WriteString(m.renderBrain(w, h))
	return b.String()
}

func (m model) mcpView() string {
	var b strings.Builder
	b.WriteString(titleStyle.Render(fmt.Sprintf(" MCP / tool calls (%d) ", len(m.mcp))) + "\n\n")
	if m.mcpErr != "" {
		b.WriteString(errStyle.Render("  "+m.mcpErr) + "\n")
		return b.String()
	}
	b.WriteString("  " + headStyle.Render(pad("TIME", 21)+pad("TOOL", 26)+pad("DIR", 6)+pad("STATUS", 10)+"ms") + "\n")
	if len(m.mcp) == 0 {
		b.WriteString(dimStyle.Render("    (no MCP calls recorded yet)") + "\n")
		return b.String()
	}
	for _, c := range m.mcp {
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
		if m.dash3D {
			keys = []string{k("←→↑↓", "rotate"), k("+/-", "zoom"), k("space", "spin"), k("v", "list"),
				k("m", "mcp"), k("a", "add node"), k("z", "brain"), k("u", "up"), k("d", "down"), k("q", "quit")}
		} else {
			keys = []string{k("↑↓", "nav"), k("⏎", "logs"), k("x", "kill"), k("v", "cluster"), k("m", "mcp"),
				k("p", "portal"), k("a", "add node"), k("z", "brain"), k("u", "up"), k("d", "down"), k("q", "quit")}
		}
	case viewConfirm:
		keys = []string{k("y", "confirm"), k("n", "cancel"), k("q", "quit")}
	case viewMcp:
		keys = []string{k("r", "refresh"), k("b", "back"), k("q", "quit")}
	case viewMenu:
		keys = []string{k("↑↓", "nav"), k("⏎", "select"), k("b", "back"), k("q", "quit")}
	case viewBrain:
		keys = []string{k("←→↑↓", "rotate"), k("+/-", "zoom"), k("space", "spin"), k("b", "back"), k("q", "quit")}
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
	p := tea.NewProgram(initialModel(), tea.WithAltScreen())
	if _, err := p.Run(); err != nil {
		fmt.Fprintln(os.Stderr, "pctl-tui error:", err)
		os.Exit(1)
	}
}
