# Thursday progress — 2026-08-06

## Scope (everything the user has asked for — verify each item below before finishing)

The SQL Studio / index-querying session. Working tree is **clean at commit `77855ac`** —
none of the items below are built yet except the deployed OpenSearch backend.

### 1. Query indexes through the frontend — CONFIRMED design: "Indexes" tab in the SQL Studio  ⏳ not started
- **User-confirmed placement:** a third sidebar tab in the SQL Studio (`ProjectData.razor`), "Indexes",
  that **lists the OpenSearch indexes**; clicking an index opens a query tab that **runs SQL against it**
  (OpenSearch SQL engine), results rendered in the same grid. This replaces the Data "Search" tab (item 2).
- Backend (all deployed): `SearchOpenSearchSqlQuery(Guid ProjectId, string Sql)` +
  `ListOpenSearchIndicesQuery` (both in `OpenSearchQueries.cs` / `OpenSearchHandlers.cs`),
  `IOpenSearchDataGateway.ListIndicesAsync` + `SearchSqlAsync`, service facades
  `IPlaceContextService.ListOpenSearchIndicesAsync` + `SearchOpenSearchSqlAsync` (`PlaceContextService.cs:168,186`),
  `OpenSearchDataGateway.SearchSqlAsync` (POST `/_plugins/_sql`, `{"query": trimmed, "fetch_size": 500}`;
  caps `MaxSqlRows=500`, `MaxSqlLength=16000`; read-only guard). None of these are called by the Host UI yet.
- UI needs: an "Indexes" sidebar tab button (like Tables/Queries), a per-tab `IsIndex` flag so the tab
  routes to `SearchOpenSearchSqlAsync` instead of `ExecuteProjectDataAsync`, and a list of index names
  loaded via `ListOpenSearchIndicesAsync` on tab open.
- OpenSearch server is TLS + auth-gated (401 on plain HTTP at `127.0.0.1:9200`); the resolver must
  already hold creds since the gateway sends only the query body.

### 2. Remove the Data "Search" tab + the "SQL AI" toolbar button  ⏳ not started
- Search tab owner: `src/PlaceContext.Host/Components/Models/DataSection.cs` — `DataSection.Search`
  (line 7) and its nav item (line 20, route `data-search` → `OpenSearchData.razor`). Remove from the
  enum + `DataSectionNavigation.Items` (the index query surface from item 1 absorbs its job).
- "SQL AI (coming soon)" button: `ProjectData.razor:88-91` — delete.

### 3. Fix the entity-browse row-click side-pane graph  ⏳ not started (verify)
- User reports the graph isn't showing on row click in their session, even though a headless
  Playwright run passed for `Cashflow` (sites + run + artifacts svg visible, `◉ Graph` toggle works).
- Relevant code: `EntityBrowse.razor:264-285` (`.dcslide.slide-560` record pane, `◉ Graph` toggle at
  275-276, graph at 321), `EntityBrowseViewModel.Records.cs` (`ShowGraph = true` default — deployed),
  `EntityBrowse.razor.css:70` (`.slide-560 { width:560px }`).
- Next: reproduce the exact DOM state at row click in a fresh headless run; check whether the pane
  opens but the graph div stays empty, or the pane doesn't open at all; check for a JS interop /
  `AfterRender` race. Playwright harness: `/home/brad/code/devcontext/scratchpad`.

### 4. Save query change  ⏳ not started — USER-CONFIRMED scope: "Implement Save query feature"
- Make the SQL Studio "Save" button functional: persist the active SQL as a **named, per-project
  saved query** (server-side, tenant-owned, `data.read`-gated).
- Make the sidebar "Queries" tab (`ProjectData.razor:24`, currently "coming soon") list saved queries
  and load one on click (fills the editor + runs or arms it).
- `Save query (coming soon)` button is at `ProjectData.razor:92-95`; "Share query (coming soon)" at
  `:96-99` — share is NOT in scope (leave, or remove if it stays a stub; confirm).
- Note: distinct from the existing **project views** feature (`SaveProjectViewAsync`, `ProjectDataViewModel.Tables.cs:281`)
  which creates real Postgres views — saved queries must NOT create DB objects.
- Needs: a `SavedQuery` entity + EF row + migration, CQRS (list/save/load/delete), `IPlaceContextService`
  facade, and UI wiring (Save → name prompt; Queries tab list → load).

### 5. Rendering charts  ⏳ not started
- The results-bar "Chart" tab is **disabled**: `ProjectData.razor:153-156`
  (`<button class="studio-results-tab" disabled>Chart</button>`). Enable it.
- Reuse `window.pcchart` (`src/PlaceContext.Host/wwwroot/pcchart.js`, Chart.js + theme tokens,
  `draw(id)` / `renderCore(id, specJson)`) and the chart-spec pattern from `ProjectAnalytics.razor`
  (`ProjectAnalyticsViewModel.CanvasId`, validated palette) to render the active tab's `ProjectQueryResult`
  as a chart (best-guess type by column shape; let the user switch type like Analytics).

### 6. The search filter  ⏳ not started
- The "Search results…" input in the results bar is **disabled**: `ProjectData.razor:130-133`.
  Enable it as a client-side filter over the active tab's result grid (`activeTab.Result.Rows`),
  case-insensitive substring across rendered cell values, re-rendering the grid on input.

### 7. The overflow of table at click to the bottom  ⏳ not started (diagnose/fix)
- Layout bug: when a table is clicked and its default `SELECT` runs, the results table overflows /
  gets clipped at the bottom of the viewport.
- Root-cause candidate found in `ProjectData.razor.css`: `.sql-studio-page { height: calc(100vh - 50px) }`
  (line 1696-1701) but the page also renders the `DataTabs` strip inside it (`ProjectData.razor:18`),
  while `.sql-studio { height: 100% }` (1703-1709) fills the full page height on top of the tabs →
  total exceeds `100vh - 50px`, and the page's `overflow: hidden` clips the bottom of `.studio-results`.
  `.studio-results { flex:1 1 40%; overflow:auto }` (2258-2263) should scroll internally once the
  heights account for the tabs strip.
- Fix direction: make `.sql-studio` `flex: 1; min-height: 0; height: auto` (or subtract the tabs strip)
  so the editor + results fill the remaining space and the grid scrolls internally, nothing clipped.
  Also coordinates with item 9 (draggable splitter) — same flex region.

### 8. Finish Monaco autocomplete for tables  ⏳ not started
- **User-confirmed:** schema-aware autocomplete for table names + columns in the SQL editor must work.
- Current state: `pcmonaco.js` (14,284 bytes) **never exposes `setSqlSchema`** — its public API is only
  `init/openFile/closeFile/setValue/getValue/destroy` (end of file ~line 345), so the schema-autocomplete
  path that commit `77855ac` claimed is dead code.
- Plan: expose `setSqlSchema` (and the completion providers that consume it) on the `window.pcmonaco`
  API; on each table tab open, push the project's table/column schema into the editor model so
  `SELECT … FROM table … WHERE col` autocompletes like the Analytics editor does. Reuse whatever schema
  data `ProjectDataViewModel` already loads (`Vm.Tables` + columns). Bump `pcmonaco.js?v=` in `App.razor:293`.

### 9. Draggable splitter between query editor and results  ⏳ not started
- **User-confirmed:** add a drag handle between the SQL editor and the results grid so the user can
  resize the split. Today `.studio-editor-shell { flex: 1 1 35% }` (line 2120-2126) and
  `.studio-results { flex: 1 1 40% }` (2258-2263) are fixed flex proportions with no divider.
- Plan: insert a thin vertical drag gutter between the two, wired to a drag-to-resize handler (pointer
  events) that sets the editor's flex-basis/height (e.g. inline `style="height:{n}px"` / CSS var) and lets
  the results fill the remainder; clamp to sensible min/max; persist per-tab or per-page choice in the
  tab state. Keep Blazor circuit-safe (no `@onmousemove` spam — use `onpointerdown` + JS or bound fields
  with throttled updates).

---

## Current verified state (this is where we start)

- **Commit:** `77855ac` "feat: OCR daemon server-side, OpenSearch SQL, Monaco schema autocomplete, ClickHouse UI" — deployed.
  Running pod image digest `sha256:daf43b1e222cef152336347b1e0b8ab10bc38868c3eff5abaaa76d26e8303fe1` (2/2 Running).
- **Deploy:** `./deploy.sh` (SSH key `$HOME/.ssh/id_ed25519` fallback; `OPENSEARCH_SYNC_HOST=root@100.116.60.120`,
  `PLACECONTEXT_DEPLOY_HOST=root@100.81.205.22`). Static assets `Cache-Control: public,max-age=3600`
  (`Program.cs:453-461`), cache-busted via `?v=` in `App.razor`; `pcmonaco.js?v=3` live in served HTML.
- **Headless verification (Playwright, scratchpad, node v26, playwright 1.62.1, chromium-1234):**
  - Login OK; shell `data-theme=dark`; Manrope + `--bg-tilt` + `--ch-yellow` + `pcmonaco.js?v=3` in served HTML.
  - SQL Studio: 34 `.resource-item` tables; `bcc_overlays` opens Monaco model `/pcdata-sql-editor/bcc_overlays`,
    ClickHouse theme bg `rgb(31,33,37)`; Run executes against the project DB (`Elapsed: 0.003s`, `Read: 0 row(s)`).
  - Entity record pane graph verified for `Cashflow` (graph svg + `◉ Graph` toggle) the run BEFORE the user
    reported it not showing in their own session.
- **OpenSearch infra (external):** `root@100.116.60.120` runs OpenSearch 2.19.5 + Dashboards 2.19.5
  (`opensearchproject/opensearch:2.19.5` Docker on `127.0.0.1:9200`); TLS + auth-gated.
- **OCR backend:** `AddOcrTracking` migration applied; `/api/ocr/pending` + `/api/ocr/complete` live (401 unauth).
- **Dead code found:** `pcmonaco.js` never exposes `setSqlSchema` — schema-aware autocomplete is dead.
  This is now an ACTIVE scope item (#8), not just a note.
- **Notes from older sessions** (`progress.md`, `progress-friday-night.md`, `progress-saturday.md`, `PROGRESS.md`):
  unrelated to this scope (2FA/communications, RBAC, jobs automation) — no save-query trace anywhere.

## Decisions locked in (user-approved this session)
- **Save query = implement the feature** (persist named per-project saved queries; Queries tab lists + loads),
  NOT just removing the stub button.
- **Index querying lives in the SQL Studio** as a third sidebar tab "Indexes" that lists indexes and runs
  OpenSearch SQL against the selected one — replaces the removed Data "Search" tab.
- **Monaco table/column autocomplete must be finished** (expose `setSqlSchema` + feed schema).
- **A draggable splitter between the editor and results** is required (not fixed flex proportions).
- "Same thing" applies to **rendering charts**, the **search filter**, and the **table overflow** — all are
  part of this session's deliverable, matching the "verify we have everything" instruction.
- SQL AI button is removed outright; Share query stays a stub unless the user says otherwise.

## Next move
1. Implement the SQL Studio core together (they share the page): item 8 (Monaco `setSqlSchema` + schema feed),
   item 9 (draggable splitter) and item 7 (overflow fix — same flex region), item 5 (Chart tab via `window.pcchart`),
   item 6 (results filter), and item 4 (Save query: entity + migration + CQRS + facade + Queries tab UI).
2. Implement item 1 (Indexes sidebar tab → `ListOpenSearchIndicesAsync` + `SearchOpenSearchSqlAsync`) and
   item 2 (remove Search tab + SQL AI button) together.
3. Debug item 3 (entity graph) with a fresh headless repro of the user's session.
4. `dotnet build PlaceContext.slnx` + targeted tests, commit, `./deploy.sh`, then Playwright smoke the live site
   (login → SQL Studio → run query → autocomplete → Save query → Queries tab → Indexes tab → run SQL on an index →
   Chart tab → results filter → drag splitter → no bottom clip).
