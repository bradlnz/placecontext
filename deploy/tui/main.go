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
	"sync/atomic"
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
	nodes       []nodeRow
	pods        []podRow
	jobs        []jobRow
	jobsErr     string
	migrateWarn string // non-empty when the DB schema is behind the app code (pending migrations)
	hostUp      int    // ready placecontext host pods
	hostTot     int
	dbUp        bool
	reach       bool
	err         string
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
	viewChat      // encrypted node-to-node chat (PCSP over mutual TLS)
	viewJoin      // connect this computer to an existing cluster with a join code
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
	runsErr   string   // sticky error for the runs list (e.g. query failed), shown in runsView
	runLinks  []string // URLs found in the currently-open run detail, openable with [o]/[1-9]

	loading bool // a data fetch (logs/mcp/metrics/search) is in flight → show a loading box

	// encrypted node-to-node chat (see chat.go)
	chatID      *chatIdentity
	chatErr     string // identity/listener startup failure, shown in the chat view
	chatPort    int
	chatEvents  chan chatEvent
	chatActive  *chatChannel
	chatPending *chatChannel // incoming request awaiting the operator's permission (y/n)
	chatPeers   []chatPeer   // nodes found by the LAN scan
	chatSel     int          // selected row in chatPeers
	chatLog     []chatLine
	chatInput   string
	chatStatus  string

	joinInput string // pasted cluster join code (viewJoin)
}

// ── messages ──────────────────────────────────────────────────────────────────────────────────
type tickMsg time.Time
type stateMsg clusterState
type actionDoneMsg struct {
	verb, output string
	err          error
}

type flashMsg string
type chatMsg chatEvent

// waitChat re-arms after every chat event so the listener/dialer goroutines can keep pumping.
func waitChat(ch chan chatEvent) tea.Cmd {
	return func() tea.Msg { return chatMsg(<-ch) }
}
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
		st.migrateWarn = mc.checkMigrations(ctx)
		return stateMsg(st)
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
	m := model{
		sp:         sp,
		kubeconfig: resolveKubeconfig(),
		pctl:       findPctl(),
		out:        viewport.New(80, 14),
		logs:       viewport.New(80, 14),
		clZoom:     1.0,
		clSpin:     true,
		chatPort:   chatDefaultPort,
		chatEvents: make(chan chatEvent, 64),
		chatStatus: "not connected",
	}
	if p, err := strconv.Atoi(os.Getenv("PCTL_CHAT_PORT")); err == nil && p > 0 {
		m.chatPort = p
	}
	id, err := loadOrCreateChatIdentity()
	if err != nil {
		m.chatErr = "chat identity: " + err.Error()
		return m
	}
	m.chatID = id
	if _, err := startChatListener(id, m.chatPort, m.chatEvents, chatBusy.Load); err != nil {
		m.chatErr = fmt.Sprintf("chat listener on :%d: %v", m.chatPort, err)
	} else {
		startChatResponder(id, m.chatPort) // answer other nodes' LAN scans
	}
	return m
}

// chatBusy mirrors "a conversation is open" for the listener goroutine, which cannot see the model.
var chatBusy atomic.Bool

func (m model) Init() tea.Cmd {
	return tea.Batch(m.sp.Tick, m.fetchState(), tick(), clusterTick(), waitChat(m.chatEvents))
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

		// Chat input also captures typing first. esc leaves the view (the conversation stays
		// open); ctrl+d hangs up; enter dials (when disconnected) or sends (when connected).
		if m.view == viewChat {
			// A pending request is modal: the operator must answer before anything else.
			if m.chatPending != nil {
				switch key {
				case "y", "Y":
					m.chatActive = m.chatPending
					m.chatPending = nil
					m.chatActive.start(m.chatEvents)
					m.chatStatus = "connected to " + m.chatActive.peerID
					m.chatLog = append(m.chatLog, chatLine{text: "— accepted " + m.chatActive.peerID, at: time.Now()})
				case "n", "N", "esc":
					go m.chatPending.close("declined")
					m.chatPending = nil
					m.chatStatus = "declined"
					chatBusy.Store(false)
				case "ctrl+c":
					m.quitting = true
					return m, tea.Quit
				}
				return m, nil
			}
			switch key {
			case "esc":
				m.view = viewDash
			case "ctrl+c":
				m.quitting = true
				return m, tea.Quit
			case "tab":
				if m.chatActive == nil && len(m.chatPeers) > 0 {
					m.chatSel = (m.chatSel + 1) % len(m.chatPeers)
				}
			case "ctrl+r":
				if m.chatID != nil && m.chatActive == nil {
					m.chatStatus = "scanning…"
					scanForPeers(m.chatID.nodeID, m.chatEvents)
				}
			case "ctrl+d":
				if m.chatActive != nil {
					ch := m.chatActive
					go ch.close("closing")
					m.chatActive = nil
					m.chatStatus = "not connected"
					m.chatLog = append(m.chatLog, chatLine{text: "— you hung up", at: time.Now()})
					chatBusy.Store(false)
				}
			case "enter":
				in := strings.TrimSpace(m.chatInput)
				if in == "" {
					// Empty ⏎ while disconnected dials the peer selected in the scan list.
					if m.chatActive == nil && m.chatID != nil && m.chatSel < len(m.chatPeers) {
						p := m.chatPeers[m.chatSel]
						m.chatStatus = "dialing " + p.hostPort + " (asking permission)…"
						dialChat(m.chatID, p.hostPort, p.nodeID, m.chatEvents)
					}
					return m, nil
				}
				if m.chatActive == nil {
					if m.chatID == nil {
						m.chatStatus = m.chatErr
						return m, nil
					}
					hostPort, nodeID, err := parseChatTarget(in)
					if err != nil {
						m.chatStatus = err.Error()
						return m, nil
					}
					m.chatStatus = "dialing " + hostPort + " (asking permission)…"
					m.chatInput = ""
					dialChat(m.chatID, hostPort, nodeID, m.chatEvents)
				} else if err := m.chatActive.send(in); err != nil {
					m.chatStatus = "send failed: " + err.Error()
				} else {
					m.chatLog = append(m.chatLog, chatLine{text: in, at: time.Now()}) // from=="" → you
					m.chatInput = ""
				}
			case "backspace":
				if r := []rune(m.chatInput); len(r) > 0 {
					m.chatInput = string(r[:len(r)-1])
				}
			default:
				// KeyRunes covers typing AND pasting (a paste arrives as one multi-rune msg —
				// exactly how a copied "host:port NODEID" target gets here).
				if msg.Type == tea.KeyRunes {
					m.chatInput += string(msg.Runes)
				} else if key == " " {
					m.chatInput += " "
				}
			}
			return m, nil
		}

		// Join-code input: paste the code from the master, ⏎ joins this computer to the cluster.
		if m.view == viewJoin {
			switch key {
			case "esc":
				m.view = viewDash
			case "ctrl+c":
				m.quitting = true
				return m, tea.Quit
			case "enter":
				if code := strings.TrimSpace(m.joinInput); code != "" && !m.busy {
					m.busy, m.busyVerb, m.view = true, "joining the cluster", viewAction
					m.out.SetContent("")
					return m, m.runAction("joining the cluster", "join", "--code", code)
				}
			case "backspace":
				if r := []rune(m.joinInput); len(r) > 0 {
					m.joinInput = string(r[:len(r)-1])
				}
			default:
				if msg.Type == tea.KeyRunes {
					m.joinInput += string(msg.Runes)
				}
			}
			return m, nil
		}

		// In a run's detail, [o] opens the first discovered link and [1-9] opens the nth. Other keys
		// fall through to scrolling, so paging still works.
		if m.view == viewRunDetail && len(m.runLinks) > 0 {
			if key == "o" {
				return m, openURL(m.runLinks[0])
			}
			if len(key) == 1 && key[0] >= '1' && key[0] <= '9' {
				if idx := int(key[0] - '1'); idx < len(m.runLinks) {
					return m, openURL(m.runLinks[idx])
				}
			}
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
			// With no cluster reachable there is no list to navigate — [j] on the welcome
			// screen means "join an existing cluster with a code" (master + agents).
			if key == "j" && m.view == viewDash && !m.state.reach && !m.busy {
				m.view, m.joinInput = viewJoin, ""
				return m, nil
			}
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
		case "u":
			// Pull the latest source from git, then rebuild and roll it out — bringing the
			// cluster up first if it isn't running (pctl update --deploy handles all of it).
			if !m.busy {
				m.busy, m.busyVerb, m.view = true, "updating + deploying", viewAction
				m.out.SetContent("")
				return m, m.runAction("updating + deploying", "update", "--deploy")
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
		case "t":
			// Encrypted chat with another node (see chat.go). Opening it scans the LAN
			// for other nodes; each side must approve before a conversation starts.
			if !m.busy {
				m.view = viewChat
				if m.chatID != nil && m.chatActive == nil && m.chatPending == nil {
					m.chatStatus = "scanning…"
					scanForPeers(m.chatID.nodeID, m.chatEvents)
				}
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

	case chatMsg:
		e := chatEvent(msg)
		switch e.kind {
		case "connected": // an outgoing dial came up — no local permission needed, we initiated
			if m.chatActive != nil && e.channel != m.chatActive {
				go e.channel.close("busy: another chat is open")
			} else {
				m.chatActive = e.channel
				m.chatStatus = "connected to " + e.channel.peerID
				m.chatLog = append(m.chatLog, chatLine{text: "— connected to " + e.channel.peerID, at: time.Now()})
				if m.view != viewChat {
					m.flash = "chat: " + e.channel.peerID[:8] + "… connected — press [t]"
				}
			}
		case "request": // an incoming caller — held unread until the operator says yes
			if m.chatActive != nil || m.chatPending != nil {
				go e.channel.close("busy: another chat is open")
			} else {
				m.chatPending = e.channel
				m.chatStatus = e.channel.peerID[:8] + "… wants to chat — [y] accept, [n] decline"
				if m.view != viewChat {
					m.flash = "chat request from " + e.channel.peerID[:8] + "… — press [t]"
				}
			}
		case "line":
			if e.channel == m.chatActive {
				m.chatLog = append(m.chatLog, e.line)
				if m.view != viewChat {
					m.flash = "chat message from " + e.line.from[:8] + "… — press [t]"
				}
			}
		case "closed":
			if e.channel == m.chatActive {
				m.chatActive = nil
				m.chatStatus = "not connected"
				m.chatLog = append(m.chatLog, chatLine{text: "— " + e.info, at: time.Now()})
			} else if e.channel == m.chatPending {
				m.chatPending = nil
				m.chatStatus = "caller hung up before you answered"
			}
		case "peers":
			m.chatPeers, m.chatSel = e.peers, 0
			if len(e.peers) == 0 {
				m.chatStatus = "scan: no other nodes found"
			} else {
				m.chatStatus = fmt.Sprintf("scan: %d node(s) found — tab selects, ⏎ chats", len(e.peers))
			}
		case "error":
			m.chatStatus = e.info
		}
		chatBusy.Store(m.chatActive != nil || m.chatPending != nil)
		return m, waitChat(m.chatEvents)

	case jobSettingMsg:
		if msg.actions != "" {
			m.setJob.postJobActions = msg.actions
		} else if msg.column == "AllowNetworkEgress" {
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
		m.runLinks = extractURLs(msg.body)
		body := msg.body
		if len(m.runLinks) > 0 {
			var b strings.Builder
			b.WriteString(body)
			b.WriteString("\n\n## Links\n\n")
			for i, u := range m.runLinks {
				if i < 9 {
					b.WriteString(fmt.Sprintf("- `[%d]` %s\n", i+1, u))
				} else {
					b.WriteString(fmt.Sprintf("- %s\n", u))
				}
			}
			b.WriteString("\n_press [o] to open the first link, [1-9] to open the nth_\n")
			body = b.String()
		}
		m.logs.SetContent(renderMarkdown(body, m.logs.Width))
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
	case viewChat:
		b.WriteString(m.chatView())
	case viewJoin:
		b.WriteString(m.joinView())
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
	// Pending-migration / stale-image skew is surfaced first and in red — it's the cause of the
	// "cannot query jobs / open run" failures, so it must stand out above transient warnings.
	if s.migrateWarn != "" {
		out.WriteString("  " + errStyle.Render("✗ "+s.migrateWarn) + "\n")
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

// joinView asks for the join code minted on the master (`pctl join-code`) and connects this
// computer to that cluster as a worker.
func (m model) joinView() string {
	var b strings.Builder
	b.WriteString(titleStyle.Render(" join cluster ") +
		dimStyle.Render("  connect this computer to an existing PlaceContext cluster") + "\n\n")
	b.WriteString("  On the " + keyStyle.Render("master") + " computer, run " + keyStyle.Render("pctl join-code") +
		" and paste the code it prints here.\n")
	b.WriteString("  " + dimStyle.Render("This machine then joins as a worker (agent) of that cluster.") + "\n\n")
	b.WriteString("  " + keyStyle.Render("code: ") + m.joinInput + dimStyle.Render("▌") + "\n")
	return b.String()
}

// setupGuide is the friendly empty state shown before any cluster exists.
func (m model) setupGuide() string {
	step := func(k, s string) string { return "   " + keyStyle.Render(k) + "  " + s + "\n" }
	var b strings.Builder
	b.WriteString(titleStyle.Render(" Welcome to PlaceContext ") + "\n\n")
	b.WriteString("  No cluster yet. Let's get you running:\n\n")
	b.WriteString(step("[u]", "Create your cluster (one key — sets up everything locally)"))
	b.WriteString(step("[j]", "Connect to an existing cluster (asks for a join code from the master)"))
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
	leftW := m.w / 2 // cluster and the node/pod/job list share the width evenly
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
		// shortName keeps the NODE column narrow so rows never wrap beside the globe.
		plain := pad(trunc(p.Name, 33), 34) + pad(p.Ready, 7) + pad(p.Status, 10) + pad(fmt.Sprintf("%d", p.Restarts), 10) + shortName(p.Node)
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
			b.WriteString("  " + pad(trunc(p.Name, 33), 34) + pad(p.Ready, 7) + st + rs + dimStyle.Render(shortName(p.Node)) + "\n")
		}
		gi++
	}
	b.WriteString("\n")

	// jobs — a job's purpose is the artifacts it generates, so that column leads; open a
	// job (⏎ → run → [o]) to get at the artifacts themselves.
	b.WriteString("  " + headStyle.Render(pad("JOB", 28)+pad("ARTIFACTS", 11)+pad("SOURCE", 8)+pad("CONC", 5)+pad("EGRESS", 8)+"UPDATED") + "\n")
	if m.state.jobsErr != "" {
		b.WriteString(dimStyle.Render("    "+m.state.jobsErr) + "\n")
	} else if len(m.state.jobs) == 0 {
		b.WriteString(dimStyle.Render("    (no jobs defined — jobs run code and generate artifacts: reports, charts, CSVs)") + "\n")
	}
	for _, j := range m.state.jobs {
		upd := j.updated
		if len(upd) > 10 {
			upd = upd[:10] // date is enough here; the run history has exact times
		}
		plain := pad(trunc(j.name, 27), 28) + pad(j.artifacts, 11) + pad(j.source, 8) + pad(j.conc, 5) + pad(j.egress, 8) + upd
		if m.selected(gi) {
			b.WriteString(selStyle.Render("❯ "+plain) + "\n")
		} else {
			arts := dimStyle.Render(pad(j.artifacts, 11))
			if j.artifacts != "-" {
				arts = okStyle.Render(pad(j.artifacts, 11))
			}
			eg := okStyle.Render(pad(j.egress, 8))
			if j.egress == "yes" {
				eg = warnStyle.Render(pad(j.egress, 8))
			}
			b.WriteString("  " + pad(trunc(j.name, 27), 28) + arts + pad(j.source, 8) + pad(j.conc, 5) + eg + dimStyle.Render(upd) + "\n")
		}
		gi++
	}
	return b.String()
}

func (m model) selected(i int) bool { return m.view == viewDash && i == m.cursor }

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

func (m model) footer() string {
	k := func(key, label string) string { return keyStyle.Render("["+key+"]") + dimStyle.Render(label) }
	var keys []string
	switch m.view {
	case viewDash:
		// The dashboard has too many shortcuts for one line — lay them out as an aligned
		// column grid sized to the terminal, so nothing wraps mid-entry.
		type kv struct{ key, label string }
		items := []kv{
			{"↑↓", "nav"}, {"⏎", "logs/runs"}, {"R", "run job"}, {"s", "settings"},
			{"x", "kill job"}, {"/", "search"}, {"g", "metrics"}, {"m", "mcp"},
			{"p", "portal"}, {"$", "subscribe"}, {"a", "add worker"}, {"u", "update+deploy"},
			{"t", "chat"}, {"c", "theme"}, {"r", "refresh"}, {"q", "quit"},
		}
		colW := 0
		for _, it := range items {
			if n := len([]rune(it.key)) + len([]rune(it.label)) + 2; n > colW {
				colW = n
			}
		}
		colW += 2
		cols := max(1, (m.w-2)/colW)
		var b strings.Builder
		b.WriteString("  ")
		for i, it := range items {
			vis := len([]rune(it.key)) + len([]rune(it.label)) + 2
			b.WriteString(k(it.key, it.label))
			if (i+1)%cols == 0 {
				if i != len(items)-1 {
					b.WriteString("\n  ")
				}
			} else {
				b.WriteString(strings.Repeat(" ", colW-vis))
			}
		}
		return b.String()
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
	case viewChat:
		if m.chatPending != nil {
			keys = []string{k("y", "accept"), k("n", "decline")}
		} else if m.chatActive != nil {
			keys = []string{k("type", "message"), k("⏎", "send"), k("ctrl+d", "hang up"), k("esc", "back")}
		} else {
			keys = []string{k("tab", "select node"), k("⏎", "chat"), k("ctrl+r", "rescan"), k("type", "manual dial"), k("esc", "back")}
		}
	case viewJoin:
		keys = []string{k("paste", "join code"), k("⏎", "join"), k("esc", "back")}
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
