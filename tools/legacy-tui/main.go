// placecontext-tui — client operator UI: install, upgrade, connect to an existing cluster.
// Build/package tooling is NOT here (see tools/pctl). This binary is what clients ship.
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
	keyStyle   = lipgloss.NewStyle().Foreground(cTeal).Bold(true)
)

type screen int

const (
	screenMenu screen = iota
	screenInstallMode
	screenConnect
	screenRunning
	screenDone
)

type model struct {
	screen  screen
	cursor  int
	input   string
	status  string
	err     string
	width   int
	height  int
	running bool
	cli     string // path to placecontext CLI
}

type runResultMsg struct {
	ok  bool
	out string
	err error
}

func main() {
	cli := os.Getenv("PLACECONTEXT_BIN")
	if cli == "" {
		cli = os.Getenv("PCTL_BIN") // legacy env from older wrappers
	}
	if cli == "" {
		if p, err := exec.LookPath("placecontext"); err == nil {
			cli = p
		} else {
			// Sibling of this binary or deploy/placecontext
			self, _ := os.Executable()
			cand := filepath.Join(filepath.Dir(self), "placecontext")
			if _, err := os.Stat(cand); err == nil {
				cli = cand
			} else {
				cand = filepath.Join(filepath.Dir(self), "..", "placecontext")
				if _, err := os.Stat(cand); err == nil {
					cli = cand
				}
			}
		}
	}
	if cli == "" {
		fmt.Fprintln(os.Stderr, "placecontext CLI not found — set PLACECONTEXT_BIN or install placecontext on PATH")
		os.Exit(1)
	}

	m := model{cli: cli, screen: screenMenu}
	if _, err := tea.NewProgram(m, tea.WithAltScreen()).Run(); err != nil {
		fmt.Fprintln(os.Stderr, err)
		os.Exit(1)
	}
}

func (m model) Init() tea.Cmd { return nil }

func (m model) Update(msg tea.Msg) (tea.Model, tea.Cmd) {
	switch msg := msg.(type) {
	case tea.WindowSizeMsg:
		m.width, m.height = msg.Width, msg.Height
		return m, nil
	case runResultMsg:
		m.running = false
		m.screen = screenDone
		if msg.err != nil {
			m.err = msg.err.Error()
			if msg.out != "" {
				m.status = msg.out
			}
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
			if msg.String() == "enter" || msg.String() == "esc" {
				m.screen = screenMenu
				m.cursor = 0
				m.status, m.err = "", ""
			}
			if msg.String() == "q" || msg.String() == "ctrl+c" {
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
		case 0: // Install
			m.screen = screenInstallMode
			m.cursor = 0
		case 1: // Upgrade
			return m, m.runCLI("upgrade")
		case 2: // Connect
			m.screen = screenConnect
			m.input = ""
		case 3: // Status
			return m, m.runCLI("status")
		case 4: // Quit
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
			return m, m.runCLI("install", "--docker")
		}
		return m, m.runCLI("install", "--service")
	}
	return m, nil
}

func (m model) updateConnect(msg tea.KeyMsg) (tea.Model, tea.Cmd) {
	switch msg.String() {
	case "esc":
		m.screen = screenMenu
		m.input = ""
	case "enter":
		code := strings.TrimSpace(m.input)
		if code == "" {
			m.err = "Paste a join code from the master (PC1.… or PC2.…)."
			return m, nil
		}
		return m, m.runCLI("connect", "--code", code)
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

func (m model) runCLI(args ...string) tea.Cmd {
	m.running = true
	m.screen = screenRunning
	m.status = "Running: placecontext " + strings.Join(args, " ")
	m.err = ""
	cli := m.cli
	return func() tea.Msg {
		cmd := exec.Command(cli, args...)
		cmd.Env = os.Environ()
		out, err := cmd.CombinedOutput()
		return runResultMsg{ok: err == nil, out: string(out), err: err}
	}
}

func (m model) View() string {
	var b strings.Builder
	b.WriteString(titleStyle.Render("PlaceContext") + "  " + dimStyle.Render("install · upgrade · connect") + "\n\n")

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
		// Cap output so the TUI stays usable
		lines := strings.Split(body, "\n")
		if len(lines) > 30 {
			lines = append(lines[:30], "…")
		}
		b.WriteString(boxStyle.Render(strings.Join(lines, "\n")) + "\n\n")
		b.WriteString(dimStyle.Render("enter back to menu  q quit") + "\n")
	}
	b.WriteString("\n" + dimStyle.Render("© Bradley Lietz / CTRL SIGNAL SOFTWARE PTY LTD") + "\n")
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
		head := it.title
		if i == m.cursor {
			head = selStyle.Render(" › " + it.title + " ")
		} else {
			head = "   " + it.title
		}
		lines = append(lines, head, dimStyle.Render("     "+it.help), "")
	}
	return strings.Join(lines, "\n")
}
