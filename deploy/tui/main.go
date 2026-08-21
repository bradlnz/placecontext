// placecontext — client operator entrypoint.
//
//   placecontext                 → full-screen TUI (default)
//   placecontext install …       → install a cluster
//   placecontext upgrade
//   placecontext connect --code …
//   placecontext status|logs|url|doctor|help|version
//
// Assets (manifests, engine, image) live under PLACECONTEXT_HOME (default ~/.placecontext).
package main

import (
	"fmt"
	"os"
	"os/exec"
	"path/filepath"
	"strings"

	tea "github.com/charmbracelet/bubbletea"
	"github.com/charmbracelet/lipgloss"
)

var (
	cTeal  = lipgloss.Color("44")
	cGreen = lipgloss.Color("42")
	cRed   = lipgloss.Color("196")
	cGray  = lipgloss.Color("245")
	cDim   = lipgloss.Color("239")

	titleStyle = lipgloss.NewStyle().Foreground(cTeal).Bold(true)
	dimStyle   = lipgloss.NewStyle().Foreground(cGray)
	okStyle    = lipgloss.NewStyle().Foreground(cGreen).Bold(true)
	errStyle   = lipgloss.NewStyle().Foreground(cRed).Bold(true)
	selStyle   = lipgloss.NewStyle().Background(lipgloss.Color("24")).Foreground(lipgloss.Color("231")).Bold(true)
	boxStyle   = lipgloss.NewStyle().Border(lipgloss.RoundedBorder()).BorderForeground(cDim).Padding(0, 1)
)

func main() {
	home := resolveHome()
	os.Setenv("PLACECONTEXT_HOME", home)

	if len(os.Args) > 1 {
		os.Exit(runCLI(home, os.Args[1:]))
	}
	// Default: TUI
	m := model{home: home, screen: screenMenu}
	if _, err := tea.NewProgram(m, tea.WithAltScreen()).Run(); err != nil {
		fmt.Fprintln(os.Stderr, err)
		os.Exit(1)
	}
}

func resolveHome() string {
	if h := os.Getenv("PLACECONTEXT_HOME"); h != "" {
		return h
	}
	// Binary next to lib/ (extracted install layout: ~/.placecontext/bin/placecontext)
	if self, err := os.Executable(); err == nil {
		self, _ = filepath.EvalSymlinks(self)
		cand := filepath.Dir(filepath.Dir(self)) // …/placecontext or …/.placecontext
		if st, err := os.Stat(filepath.Join(cand, "lib")); err == nil && st.IsDir() {
			return cand
		}
		// …/bin/placecontext → parent
		parent := filepath.Dir(self)
		if st, err := os.Stat(filepath.Join(parent, "lib")); err == nil && st.IsDir() {
			return parent
		}
	}
	home, _ := os.UserHomeDir()
	return filepath.Join(home, ".placecontext")
}

func runCLI(home string, args []string) int {
	// Global -v / -vv / --verbose may appear before the subcommand.
	verbose := 0
	if v := os.Getenv("PLACECONTEXT_VERBOSE"); v != "" {
		switch v {
		case "1", "true", "yes", "on":
			verbose = 1
		case "2":
			verbose = 2
		}
	}
	filtered := make([]string, 0, len(args))
	for _, a := range args {
		switch a {
		case "-v", "--verbose":
			verbose++
		case "-vv":
			verbose = 2
		default:
			filtered = append(filtered, a)
		}
	}
	args = filtered
	if len(args) == 0 {
		printHelp()
		return 0
	}
	if verbose > 0 {
		_ = os.Setenv("PLACECONTEXT_VERBOSE", fmt.Sprintf("%d", verbose))
	}

	cmd := args[0]
	rest := args[1:]
	switch cmd {
	case "help", "-h", "--help":
		printHelp()
		return 0
	case "version", "--version":
		v := readVersion(home)
		fmt.Println(v)
		return 0
	case "tui":
		m := model{home: home, screen: screenMenu}
		if _, err := tea.NewProgram(m, tea.WithAltScreen()).Run(); err != nil {
			fmt.Fprintln(os.Stderr, err)
			return 1
		}
		return 0
	case "install", "upgrade", "connect", "join-code", "status", "logs", "url", "doctor":
		engineArgs := append([]string{cmd}, rest...)
		// Re-append verbose so the bash engine also sees the flag on the subcommand.
		for i := 0; i < verbose; i++ {
			engineArgs = append(engineArgs, "-v")
		}
		return runEngine(home, engineArgs...)
	default:
		fmt.Fprintf(os.Stderr, "unknown command: %s\n\n", cmd)
		printHelp()
		return 2
	}
}

func printHelp() {
	fmt.Print(`placecontext — PlaceContext operator

USAGE
  placecontext [-v|-vv] <command> …

  placecontext                 Open the TUI (install / upgrade / connect)
  placecontext install [--docker|--service] [-v]
  placecontext upgrade [--code CODE [--ts-authkey KEY]] [-v]
  placecontext connect --code CODE [--ts-authkey KEY] [-v]
  placecontext join-code [--ts-authkey KEY]
  placecontext status | logs [-f] | url | doctor [-v]
  placecontext version
  placecontext help

CONNECT (fleet join — no tools/pctl needed)
  On master:   sudo placecontext join-code   (add --ts-authkey KEY for a Mac worker)
  Linux host:  sudo placecontext connect --code PC2.…
  Mac host:         placecontext connect --code PC2.…   (k3s agent in Docker; no sudo)
  Keyless code / rotating key:  connect --code PC1.… --ts-authkey KEY
  Upgrade + (re)join agent:     sudo placecontext upgrade --code PC2.… [--ts-authkey KEY]

VERBOSITY
  -v, --verbose   stream kubectl/k3d output; extra step detail
  -vv             more diagnostics
  PLACECONTEXT_VERBOSE=1|2

  Install/upgrade always print step progress (secrets, manifests, waits, pods).

Install the CLI itself with:
  curl -fsSL https://get.placecontext.io/install.sh | bash

Your data and jobs are yours. PlaceContext is MIT licensed.
`)
}

func readVersion(home string) string {
	b, err := os.ReadFile(filepath.Join(home, "VERSION"))
	if err != nil {
		return "dev"
	}
	return strings.TrimSpace(string(b))
}

// runEngine shells to the bash engine shipped under $HOME/lib/placecontext-engine,
// falling back to a repo-tree deploy/placecontext when developing from source.
func runEngine(home string, args ...string) int {
	engine := filepath.Join(home, "lib", "placecontext-engine")
	if st, err := os.Stat(engine); err != nil || st.IsDir() {
		// Dev tree: walk up from executable / cwd looking for deploy/placecontext
		for _, cand := range []string{
			filepath.Join(home, "deploy", "placecontext"),
			"deploy/placecontext",
		} {
			if st, err := os.Stat(cand); err == nil && !st.IsDir() {
				engine = cand
				break
			}
		}
	}
	if st, err := os.Stat(engine); err != nil || st.IsDir() {
		fmt.Fprintf(os.Stderr, "placecontext engine not found at %s\n", engine)
		fmt.Fprintln(os.Stderr, "Re-run the installer: curl -fsSL …/deploy/install.sh | bash")
		return 1
	}
	cmd := exec.Command(engine, args...)
	cmd.Stdin = os.Stdin
	cmd.Stdout = os.Stdout
	cmd.Stderr = os.Stderr
	cmd.Env = append(os.Environ(),
		"PLACECONTEXT_HOME="+home,
		"PCTL_IMAGE_TAR="+filepath.Join(home, "lib", "placecontext-local.tar"),
		"K3S_DIR="+filepath.Join(home, "lib", "k3s"),
	)
	// Engine expects ROOT; point at home so relative deploy/ paths can be remapped.
	if err := cmd.Run(); err != nil {
		if ee, ok := err.(*exec.ExitError); ok {
			return ee.ExitCode()
		}
		fmt.Fprintln(os.Stderr, err)
		return 1
	}
	return 0
}

// ── TUI ─────────────────────────────────────────────────────────────────────────────────────────

type screen int

const (
	screenMenu screen = iota
	screenInstallMode
	screenConnect
	screenRunning
	screenDone
)

type model struct {
	home    string
	screen  screen
	cursor  int
	input   string
	status  string
	err     string
	running bool
}

type runResultMsg struct {
	out string
	err error
}

func (m model) Init() tea.Cmd { return nil }

func (m model) Update(msg tea.Msg) (tea.Model, tea.Cmd) {
	switch msg := msg.(type) {
	case runResultMsg:
		m.running = false
		m.screen = screenDone
		if msg.err != nil {
			m.err = msg.err.Error()
			m.status = msg.out
		} else {
			m.err = ""
			m.status = msg.out
			if m.status == "" {
				m.status = "Done."
			}
		}
		return m, nil
	case tea.KeyMsg:
		if m.running {
			if msg.String() == "ctrl+c" {
				return m, tea.Quit
			}
			return m, nil
		}
		switch m.screen {
		case screenMenu:
			return m.updateMenu(msg)
		case screenInstallMode:
			return m.updateInstallMode(msg)
		case screenConnect:
			return m.updateConnect(msg)
		case screenDone:
			switch msg.String() {
			case "enter", "esc":
				m.screen = screenMenu
				m.cursor = 0
				m.status, m.err = "", ""
			case "q", "ctrl+c":
				return m, tea.Quit
			}
		}
	}
	return m, nil
}

func (m model) updateMenu(msg tea.KeyMsg) (tea.Model, tea.Cmd) {
	items := 5
	switch msg.String() {
	case "q", "ctrl+c":
		return m, tea.Quit
	case "up", "k":
		if m.cursor > 0 {
			m.cursor--
		}
	case "down", "j":
		if m.cursor < items-1 {
			m.cursor++
		}
	case "enter":
		switch m.cursor {
		case 0:
			m.screen = screenInstallMode
			m.cursor = 0
		case 1:
			return m.startRun("upgrade")
		case 2:
			m.screen = screenConnect
			m.input = ""
			m.err = ""
		case 3:
			return m.startRun("status")
		case 4:
			return m, tea.Quit
		}
	}
	return m, nil
}

func (m model) updateInstallMode(msg tea.KeyMsg) (tea.Model, tea.Cmd) {
	switch msg.String() {
	case "esc":
		m.screen = screenMenu
		m.cursor = 0
	case "up", "k":
		if m.cursor > 0 {
			m.cursor--
		}
	case "down", "j":
		if m.cursor < 1 {
			m.cursor++
		}
	case "enter":
		if m.cursor == 0 {
			return m.startRun("install", "--docker")
		}
		return m.startRun("install", "--service")
	}
	return m, nil
}

func (m model) updateConnect(msg tea.KeyMsg) (tea.Model, tea.Cmd) {
	switch msg.String() {
	case "esc":
		m.screen = screenMenu
		m.input = ""
		m.err = ""
	case "enter":
		code := strings.TrimSpace(m.input)
		if code == "" {
			m.err = "Paste a join code from the master (PC1.… or PC2.…)."
			return m, nil
		}
		return m.startRun("connect", "--code", code)
	case "backspace":
		if len(m.input) > 0 {
			m.input = m.input[:len(m.input)-1]
		}
	default:
		if len(msg.String()) == 1 {
			m.input += msg.String()
		} else if msg.Type == tea.KeyRunes {
			m.input += string(msg.Runes)
		}
	}
	return m, nil
}

func (m model) startRun(args ...string) (tea.Model, tea.Cmd) {
	m.running = true
	m.screen = screenRunning
	m.status = "Running: placecontext " + strings.Join(args, " ")
	m.err = ""
	home := m.home
	return m, func() tea.Msg {
		// Capture combined output without hijacking the TUI's stdout permanently.
		engine := filepath.Join(home, "lib", "placecontext-engine")
		if st, err := os.Stat(engine); err != nil || st.IsDir() {
			for _, cand := range []string{
				filepath.Join(home, "deploy", "placecontext"),
				"deploy/placecontext",
			} {
				if st, err := os.Stat(cand); err == nil && !st.IsDir() {
					engine = cand
					break
				}
			}
		}
		// Prefer verbose engine output in the TUI so long installs show real progress when done.
		cmdArgs := append([]string{}, args...)
		if os.Getenv("PLACECONTEXT_VERBOSE") == "" {
			cmdArgs = append(cmdArgs, "-v")
		}
		cmd := exec.Command(engine, cmdArgs...)
		cmd.Env = append(os.Environ(),
			"PLACECONTEXT_HOME="+home,
			"PCTL_IMAGE_TAR="+filepath.Join(home, "lib", "placecontext-local.tar"),
			"K3S_DIR="+filepath.Join(home, "lib", "k3s"),
			"PLACECONTEXT_VERBOSE=1",
		)
		out, err := cmd.CombinedOutput()
		return runResultMsg{out: string(out), err: err}
	}
}

func (m model) View() string {
	var b strings.Builder
	ver := readVersion(m.home)
	b.WriteString(titleStyle.Render("PlaceContext") + "  " + dimStyle.Render(ver+" · install · upgrade · connect") + "\n\n")

	switch m.screen {
	case screenMenu:
		b.WriteString(boxStyle.Render(m.menuView()) + "\n\n")
		b.WriteString(dimStyle.Render("↑↓ move  enter select  q quit") + "\n")
	case screenInstallMode:
		b.WriteString(titleStyle.Render("How do you want to install?") + "\n\n")
		b.WriteString(boxStyle.Render(m.installView()) + "\n\n")
		b.WriteString(dimStyle.Render("↑↓ move  enter start  esc back") + "\n")
	case screenConnect:
		b.WriteString(titleStyle.Render("Connect to an existing cluster") + "\n\n")
		b.WriteString("Join code:\n")
		b.WriteString(selStyle.Render(m.input+"█") + "\n\n")
		if m.err != "" {
			b.WriteString(errStyle.Render(m.err) + "\n\n")
		}
		b.WriteString(dimStyle.Render("enter connect  esc back") + "\n")
	case screenRunning:
		b.WriteString(boxStyle.Render(m.status+"\n\nWorking…") + "\n")
	case screenDone:
		body := m.status
		if m.err != "" {
			body = errStyle.Render("Failed: "+m.err) + "\n\n" + body
		} else {
			body = okStyle.Render("Success") + "\n\n" + body
		}
		lines := strings.Split(body, "\n")
		// Keep enough lines that install step progress remains visible after a long run.
		if len(lines) > 80 {
			lines = append([]string{"… (earlier output truncated)"}, lines[len(lines)-79:]...)
		}
		b.WriteString(boxStyle.Render(strings.Join(lines, "\n")) + "\n\n")
		b.WriteString(dimStyle.Render("enter back to menu  q quit") + "\n")
	}
	b.WriteString("\n" + dimStyle.Render("MIT licensed · your data & jobs are yours") + "\n")
	return b.String()
}

func (m model) menuView() string {
	items := []string{
		"Install PlaceContext",
		"Upgrade",
		"Connect to existing cluster",
		"Status",
		"Quit",
	}
	var lines []string
	for i, it := range items {
		if i == m.cursor {
			lines = append(lines, selStyle.Render(" › "+it+" "))
		} else {
			lines = append(lines, "   "+it)
		}
	}
	return strings.Join(lines, "\n")
}

func (m model) installView() string {
	items := []struct{ title, help string }{
		{"In Docker (k3d)", "Laptop / single machine — k3s-in-Docker, easy to tear down"},
		{"As a system service (k3s)", "Server / fleet master — needs sudo; survives reboots"},
	}
	var lines []string
	for i, it := range items {
		head := "   " + it.title
		if i == m.cursor {
			head = selStyle.Render(" › " + it.title + " ")
		}
		lines = append(lines, head, dimStyle.Render("     "+it.help), "")
	}
	return strings.Join(lines, "\n")
}
