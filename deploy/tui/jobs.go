package main

import (
	"context"
	"encoding/json"
	"regexp"
	"strconv"
	"strings"
	"time"

	tea "github.com/charmbracelet/bubbletea"
)

type jobRow struct {
	id, tenant, name, source, conc, egress, updated, timeout string
	postJobActions                                           string // JSON array, e.g. ["HtmlReport","Chart"]; empty = none
	artifacts                                                string // artifacts produced across this job's runs — the job's raison d'être
}

// uuidRe matches a canonical UUID. All ids/tenant ids interpolated into psql come from the jobs table
// (they're DB-generated UUID columns), but we validate the shape before building any SQL as defence in
// depth: a value that passes this can contain no quote or SQL metacharacter, so it cannot break out of
// the literal. Anything else is rejected before a query is built. (Column names interpolated elsewhere
// come only from the hard-coded jobSettings() list, never from user or DB input.)
var uuidRe = regexp.MustCompile(`^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$`)

func validUUID(s string) bool { return uuidRe.MatchString(s) }

// querySection separates the batched psql results below; it can never appear in real row data
// (job names/ids contain no such marker and it occupies a whole line by itself).
const querySection = "@@PC-SECTION@@"

// queryJobs fetches the jobs list, the applied-migration id, and per-job artifact counts in ONE
// kubectl exec — each exec is a full API-server round trip (hundreds of ms), and this runs on the
// ~1.5s dashboard refresh, so the batch is what keeps the jobs list feeling live.
// Returns (rows, jobsErr, migrateWarn).
func (m model) queryJobs(ctx context.Context) ([]jobRow, string, string) {
	const base = `"Id", "TenantId", "Name", "MapSourceKind", "ConcurrencyLimit", "AllowNetworkEgress", "UpdatedAt"`
	run := func(sel string) ([]byte, error) {
		// ON_ERROR_STOP makes psql abort at the first failing -c (surfacing it on stderr) while the
		// earlier -c results are already printed — so a missing table only costs its own section.
		// Section order puts the fallible artifact-count query LAST: on an old DB without that table
		// the jobs list and the migration warning (the signal that explains it) still come through.
		return m.kubectl(ctx, "-n", ns, "exec", "deploy/placecontext-db", "--",
			"psql", "-U", "postgres", "-d", "placecontext", "-v", "ON_ERROR_STOP=1", "-At", "-F", "\t",
			"-c", `SELECT `+sel+` FROM jobs ORDER BY "UpdatedAt" DESC LIMIT 100`,
			"-c", `SELECT '`+querySection+`'`,
			"-c", `SELECT coalesce(max("MigrationId"),'') FROM "__EFMigrationsHistory"`,
			"-c", `SELECT '`+querySection+`'`,
			"-c", `SELECT r."JobId", count(*) FROM job_run_artifacts a JOIN job_runs r ON a."RunId" = r."Id" GROUP BY r."JobId"`)
	}
	// TimeoutSeconds and PostJobActionsJson are newer columns; if the DB is behind the app code
	// (migration not yet applied) fall back progressively so the jobs list still works — the
	// migration banner tells the user to deploy. Only a missing COLUMN retries; a missing
	// relation (the artifact-counts table on an old DB) keeps the partial output.
	b, err := run(base + `, "TimeoutSeconds", "PostJobActionsJson"`)
	if err != nil && strings.Contains(err.Error(), "column") && strings.Contains(err.Error(), "does not exist") {
		b, err = run(base + `, "TimeoutSeconds"`)
		if err != nil && strings.Contains(err.Error(), "column") && strings.Contains(err.Error(), "does not exist") {
			b, err = run(base) // 7 cols; the row padding below defaults the newer fields
		}
	}

	// Split the batched output into its sections: jobs rows / migration id / artifact counts.
	sections := [][]string{{}}
	for _, ln := range strings.Split(strings.TrimSpace(string(b)), "\n") {
		switch {
		case ln == querySection:
			sections = append(sections, []string{})
		case ln != "":
			sections[len(sections)-1] = append(sections[len(sections)-1], ln)
		}
	}
	if err != nil && len(sections[0]) == 0 {
		return nil, "could not query jobs: " + err.Error(), ""
	}

	applied := ""
	if len(sections) > 1 && len(sections[1]) > 0 {
		applied = strings.TrimSpace(sections[1][0])
	}

	// Jobs exist to generate artifacts — count what each job has produced so the list leads
	// with output, not config. Older DBs without the artifacts table just show "-".
	counts := map[string]string{}
	if len(sections) > 2 {
		for _, ln := range sections[2] {
			if f := strings.Split(ln, "\t"); len(f) == 2 {
				counts[f[0]] = f[1]
			}
		}
	}

	var rows []jobRow
	for _, ln := range sections[0] {
		f := strings.Split(ln, "\t")
		for len(f) < 9 {
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
		acts := f[8]
		if strings.TrimSpace(acts) == "" {
			acts = "[]"
		}
		arts := counts[f[0]]
		if arts == "" {
			arts = "-"
		}
		rows = append(rows, jobRow{f[0], f[1], f[2], f[3], f[4], eg, upd, to, acts, arts})
	}
	return rows, "", migrationWarning(applied)
}

// expectedMigration is the newest EF migration id in the source tree at TUI build time, stamped via
// -ldflags "-X main.expectedMigration=<id>" (see the Makefile). Empty for a plain `go build`, in
// which case the migration check is skipped (no false warnings).
var expectedMigration string

// migrationWarning compares the newest migration applied to the live DB (fetched in the queryJobs
// batch) against the one this TUI was built from. A DB that is behind means the deployed app image
// predates pending migrations and needs (re)deploying — exactly the skew that makes newer columns
// (e.g. TimeoutSeconds) missing. Returns a human-readable warning, or "" when up to date / unknown.
func migrationWarning(applied string) string {
	if expectedMigration == "" || applied == "" {
		return ""
	}
	// Migration ids are timestamp-prefixed, so lexical order == chronological order.
	if applied < expectedMigration {
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
		if !validUUID(j.id) || !validUUID(j.tenant) {
			return flashMsg("could not run job — invalid id/tenant (refresh and retry)")
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

// jobSetting is one toggleable (checkbox) job setting in the settings view ([s]). It maps either to a
// boolean column on the jobs table (column set) or to a post-job action kind held in the
// PostJobActionsJson array column (action set). Toggling persists immediately and takes effect on the
// next run.
type jobSetting struct {
	label, desc, column, action string
	get                         func(jobRow) bool
}

// postJobActionKinds are the action names recognised by the server (PostJobActionKind). The TUI only
// ever writes values from this fixed list into PostJobActionsJson, so no untrusted text reaches SQL.
var postJobActionKinds = []string{"HtmlReport", "Chart", "Csv", "RawBundle"}

func jobSettings() []jobSetting {
	return []jobSetting{
		{label: "Allow network egress",
			desc:   "Let this job's containers reach the network. When off, a deny-all NetworkPolicy isolates the run.",
			column: "AllowNetworkEgress", get: func(j jobRow) bool { return j.egress == "yes" }},
		{label: "Post-job: HTML report",
			desc:   "After each run, render the run's data as a styled HTML page (LLM-generated, deterministic fallback).",
			action: "HtmlReport", get: jobHasAction("HtmlReport")},
		{label: "Post-job: chart",
			desc:   "After each run, draw the run's data as an inline-SVG chart.",
			action: "Chart", get: jobHasAction("Chart")},
		{label: "Post-job: CSV export",
			desc:   "After each run, flatten the run's artifacts into a downloadable CSV.",
			action: "Csv", get: jobHasAction("Csv")},
		{label: "Post-job: raw bundle",
			desc:   "After each run, store every produced output file as-is for download.",
			action: "RawBundle", get: jobHasAction("RawBundle")},
	}
}

// jobHasAction reports whether a job's PostJobActionsJson array contains the given action kind.
func jobHasAction(kind string) func(jobRow) bool {
	return func(j jobRow) bool {
		for _, a := range parseActions(j.postJobActions) {
			if a == kind {
				return true
			}
		}
		return false
	}
}

// parseActions decodes the PostJobActionsJson array, keeping only recognised kinds (defensive: ignores
// anything unexpected so a stray value can never be echoed back into a write).
func parseActions(s string) []string {
	var raw []string
	if strings.TrimSpace(s) == "" || json.Unmarshal([]byte(s), &raw) != nil {
		return nil
	}
	var out []string
	for _, r := range raw {
		for _, k := range postJobActionKinds {
			if r == k {
				out = append(out, k)
				break
			}
		}
	}
	return out
}

type jobSettingMsg struct {
	column  string
	val     bool
	actions string // new PostJobActionsJson when this toggled a post-job action; "" otherwise
	flash   string
}

// toggleJobSettingCmd flips a job setting in the jobs table via psql, off the UI thread — either a
// boolean column or membership of a post-job action kind in PostJobActionsJson.
func (m model) toggleJobSettingCmd(j jobRow, s jobSetting, val bool) tea.Cmd {
	mc := m
	return func() tea.Msg {
		if !validUUID(j.id) {
			return flashMsg("could not update setting — invalid job id (refresh and retry)")
		}
		ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
		defer cancel()

		var setClause string
		state := "off"
		var newActions string
		if s.action != "" {
			// Rebuild the action set from the recognised kinds only, then add/remove this one.
			has := map[string]bool{}
			for _, a := range parseActions(j.postJobActions) {
				has[a] = true
			}
			has[s.action] = val
			if val {
				state = "on"
			}
			var keep []string
			for _, k := range postJobActionKinds { // stable, allowlisted order
				if has[k] {
					keep = append(keep, k)
				}
			}
			if keep == nil {
				keep = []string{}
			}
			enc, _ := json.Marshal(keep) // values are fixed identifiers — no quotes/metacharacters
			newActions = string(enc)
			setClause = `"PostJobActionsJson"='` + newActions + `'`
		} else {
			b := "false"
			if val {
				b, state = "true", "on"
			}
			setClause = `"` + s.column + `"=` + b
		}

		q := `UPDATE jobs SET ` + setClause + `, "UpdatedAt"=now() WHERE "Id"='` + j.id + `'`
		if _, err := mc.kubectl(ctx, "-n", ns, "exec", "deploy/placecontext-db", "--",
			"psql", "-U", "postgres", "-d", "placecontext", "-c", q); err != nil {
			return flashMsg(s.label + " toggle failed: " + err.Error())
		}
		return jobSettingMsg{column: s.column, val: val, actions: newActions, flash: s.label + ": " + state}
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
		if !validUUID(j.id) {
			return flashMsg("could not update timeout — invalid job id (refresh and retry)")
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
