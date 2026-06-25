package main

import (
	"crypto/hmac"
	"crypto/sha256"
	"encoding/base64"
	"strconv"
	"strings"
	"testing"
	"time"

	tea "github.com/charmbracelet/bubbletea"
)

// press sends a single key to the model and returns the updated model.
func press(m model, key string) model {
	var msg tea.Msg
	switch key {
	case "enter":
		msg = tea.KeyMsg{Type: tea.KeyEnter}
	case "up":
		msg = tea.KeyMsg{Type: tea.KeyUp}
	case "down":
		msg = tea.KeyMsg{Type: tea.KeyDown}
	default:
		msg = tea.KeyMsg{Type: tea.KeyRunes, Runes: []rune(key)}
	}
	nm, _ := m.Update(msg)
	return nm.(model)
}

func withState(m model) model {
	st := clusterState{
		reach:   true,
		nodes:   []nodeRow{{Name: "server-0", Role: "server", Status: "Ready"}},
		pods:    []podRow{{Name: "host-1", Ready: "1/1", Status: "Running"}, {Name: "db-1", Ready: "1/1", Status: "Running"}},
		jobs:    []jobRow{{name: "nightly-rollup", source: "image", conc: "1", egress: "no", updated: "2026-06-26"}},
		hostUp:  1,
		hostTot: 1,
		dbUp:    true,
	}
	nm, _ := m.Update(stateMsg(st))
	return nm.(model)
}

// list is now a no-op: the dashboard always shows the selectable list beneath the cluster.
func list(m model) model { return m }

func TestKeyDispatchSwitchesViews(t *testing.T) {
	base := list(withState(initialModel()))
	cases := []struct {
		key  string
		want view
	}{
		{"z", viewBrain},
		{"a", viewMenu},
		{"x", viewConfirm}, // kill the selected row → confirmation gate
	}
	for _, c := range cases {
		if got := press(base, c.key).view; got != c.want {
			t.Errorf("key %q: view = %d, want %d", c.key, got, c.want)
		}
	}
}

func TestDashboardNavigationIncludesJobs(t *testing.T) {
	m := list(withState(initialModel())) // 1 node + 2 pods + 1 job = 4 selectable rows
	if got := len(m.sel); got != 4 {
		t.Fatalf("selectable rows = %d, want 4 (nodes+pods+jobs)", got)
	}
	for i := 0; i < 5; i++ {
		m = press(m, "down")
	}
	if m.cursor != 3 { // clamps at last row (the job)
		t.Errorf("cursor = %d, want clamp at 3", m.cursor)
	}
	if m.sel[m.cursor].kind != "job" {
		t.Errorf("last row kind = %q, want job", m.sel[m.cursor].kind)
	}
}

func TestKillConfirmFlow(t *testing.T) {
	m := list(withState(initialModel()))
	// move to the job row and arm kill
	for i := 0; i < 3; i++ {
		m = press(m, "down")
	}
	m = press(m, "x")
	if m.view != viewConfirm {
		t.Fatalf("x did not open confirm gate")
	}
	if want := []string{"kill", "job", "nightly-rollup", "--yes"}; !equal(m.confirmArgs, want) {
		t.Errorf("confirmArgs = %v, want %v", m.confirmArgs, want)
	}
	// cancel returns to dashboard
	m = press(m, "n")
	if m.view != viewDash {
		t.Errorf("after cancel view = %d, want dashboard", m.view)
	}
}

func TestClusterTopologyRenders(t *testing.T) {
	m := withState(initialModel())
	m.w, m.h = 100, 34
	out := m.clusterPanel(13)
	if len(out) == 0 {
		t.Fatal("clusterPanel produced empty frame")
	}
	// the live server name should appear as the planet label
	if !strings.Contains(out, "server-0") {
		t.Errorf("cluster panel missing server label; got:\n%s", out)
	}
	// and the full dashboard should include the list beneath (a pod row)
	if !strings.Contains(m.dashboard(), "host-1") {
		t.Errorf("dashboard list should appear beneath the cluster")
	}
}

func equal(a, b []string) bool {
	if len(a) != len(b) {
		return false
	}
	for i := range a {
		if a[i] != b[i] {
			return false
		}
	}
	return true
}

func TestEnterOnRowOpensLogs(t *testing.T) {
	m := list(withState(initialModel()))
	m = press(m, "down") // select pod "host-1"
	m = press(m, "enter")
	if m.view != viewLogs {
		t.Fatalf("after enter view = %d, want viewLogs", m.view)
	}
}

func TestBackReturnsToDashboard(t *testing.T) {
	m := withState(initialModel())
	m = press(m, "z")
	if m.view != viewBrain {
		t.Fatalf("expected brain view")
	}
	m = press(m, "b")
	if m.view != viewDash {
		t.Errorf("after back view = %d, want viewDash", m.view)
	}
}

func TestMintPortalToken(t *testing.T) {
	const key = "test-signing-key"
	now := time.Unix(1_700_000_000, 0)
	tok := mintPortalToken(key, now)

	// Shape: payload "<exp>.<nonce>" plus the signature segment.
	parts := strings.Split(tok, ".")
	if len(parts) != 3 {
		t.Fatalf("token has %d segments, want 3: %q", len(parts), tok)
	}

	exp, err := strconv.ParseInt(parts[0], 10, 64)
	if err != nil {
		t.Fatalf("exp not an int: %v", err)
	}
	if want := now.Add(60 * time.Second).Unix(); exp != want {
		t.Errorf("exp = %d, want %d (now+60s)", exp, want)
	}

	// Signature must verify against the host's scheme: base64url-unpadded HMAC-SHA256 over "<exp>.<nonce>".
	mac := hmac.New(sha256.New, []byte(key))
	mac.Write([]byte(parts[0] + "." + parts[1]))
	if want := base64.RawURLEncoding.EncodeToString(mac.Sum(nil)); parts[2] != want {
		t.Errorf("signature = %q, want %q", parts[2], want)
	}

	// Nonce is fresh per mint, so two tokens differ.
	if tok == mintPortalToken(key, now) {
		t.Error("two mints produced identical tokens — nonce is not random")
	}
}

func TestBrainGeneratesPoints(t *testing.T) {
	if n := len(generateBrain()); n < 500 {
		t.Errorf("brain point cloud too small: %d points", n)
	}
}

func TestRenderBrainProducesFrame(t *testing.T) {
	m := initialModel()
	m.w, m.h = 80, 30
	out := m.renderBrain(70, 22)
	if len(out) == 0 {
		t.Fatal("renderBrain returned empty frame")
	}
}
