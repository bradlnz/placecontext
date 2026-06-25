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

func brainTick() tea.Cmd {
	return tea.Tick(80*time.Millisecond, func(t time.Time) tea.Msg { return brainTickMsg(t) })
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
	}
}

func (m model) Init() tea.Cmd {
	return tea.Batch(m.sp.Tick, m.fetchState(), tick())
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
			if m.view == viewDash && m.cursor > 0 {
				m.cursor--
			}
		case "down", "j":
			if m.view == viewDash && m.cursor < len(m.sel)-1 {
				m.cursor++
			}
		case "enter":
			if m.view == viewDash && m.cursor < len(m.sel) {
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
		keys = []string{k("↑↓", "nav"), k("⏎", "logs"), k("x", "kill"), k("p", "portal"), k("a", "add node"), k("z", "brain"),
			k("u", "up"), k("d", "down"), k("r", "refresh"), k("q", "quit")}
	case viewConfirm:
		keys = []string{k("y", "confirm"), k("n", "cancel"), k("q", "quit")}
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
