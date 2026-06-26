package main

import (
	"context"
	"strconv"
	"strings"
	"time"

	tea "github.com/charmbracelet/bubbletea"
)

type jobRow struct{ id, tenant, name, source, conc, egress, updated, timeout string }

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

// expectedMigration is the newest EF migration id in the source tree at TUI build time, stamped via
// -ldflags "-X main.expectedMigration=<id>" (see the Makefile). Empty for a plain `go build`, in
// which case the migration check is skipped (no false warnings).
var expectedMigration string

// checkMigrations compares the newest migration applied to the live DB against the one this TUI was
// built from. A DB that is behind means the deployed app image predates pending migrations and needs
// (re)deploying — exactly the skew that makes newer columns (e.g. TimeoutSeconds) missing. Returns a
// human-readable warning, or "" when up to date / not determinable.
func (m model) checkMigrations(ctx context.Context) string {
	if expectedMigration == "" {
		return ""
	}
	b, err := m.kubectl(ctx, "-n", ns, "exec", "deploy/placecontext-db", "--",
		"psql", "-U", "postgres", "-d", "placecontext", "-At", "-c",
		`SELECT coalesce(max("MigrationId"),'') FROM "__EFMigrationsHistory"`)
	if err != nil {
		return "" // DB unreachable / not initialised — other alerts cover that
	}
	applied := strings.TrimSpace(string(b))
	// Migration ids are timestamp-prefixed, so lexical order == chronological order.
	if applied != "" && applied < expectedMigration {
		return "DB schema is behind app code (applied " + shortMig(applied) +
			", expected " + shortMig(expectedMigration) + ") — run `pctl deploy` to apply migrations"
	}
	return ""
}

// shortMig drops the timestamp prefix from a migration id for display:
// "20260626040930_AddJobTimeout" → "AddJobTimeout".
func shortMig(id string) string {
	if i := strings.IndexByte(id, '_'); i >= 0 {
		return id[i+1:]
	}
	return id
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
