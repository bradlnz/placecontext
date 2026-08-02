# Enterprise and responsive UI research

Timestamp: 2026-08-02T06:04:03Z

## Decision

Modernise PlaceContext as a **productive enterprise control plane**, not as a marketing dashboard and not as a visual clone of one product.

The recommended direction combines:

- **IBM Carbon** for productive density, data-table anatomy, accessibility, and shell discipline;
- **PatternFly** for operational workflows, responsive data presentation, drawers, empty states, and wizards;
- **Linear** for command search, compact navigation, keyboard efficiency, and restrained visual noise;
- **Sentry** for run/incident triage, filterable operational views, and status-led information hierarchy;
- **Stripe Dashboard** for form clarity, contextual guidance, conservative radii, and polished interaction states;
- **Salesforce Lightning** for record-centric workspaces, activity history, process stages, and administrative discoverability.

PlaceContext should retain its own identity. In particular, the current command search, project context, dark theme, technical data density, job-status semantics, and artifact focus are useful foundations.

Responsive behaviour is a release requirement for every slice. Desktop is not the canonical implementation with mobile added afterward.

## Evidence collected

The audit used the locally running Blazor application in Development mode and Playwright with real rendering at:

- desktop: 1440 × 1000;
- mobile: 390 × 844 with touch emulation.

Routes audited:

1. Dashboard
2. Chat
3. Artifacts
4. Observability
5. Projects overview
6. Onboarding
7. Wiki
8. About
9. Mobile navigation drawer

No browser console or page errors occurred in these captures.

Evidence:

- [Desktop contact sheet](../assets/ui-audit-2026-08-02/desktop-contact-sheet.png)
- [Mobile contact sheet](../assets/ui-audit-2026-08-02/mobile-contact-sheet.png)
- [Desktop dashboard](../assets/ui-audit-2026-08-02/dashboard-desktop.png)
- [Mobile dashboard](../assets/ui-audit-2026-08-02/dashboard-mobile.png)

Code inspection found 48 scoped CSS files in the Host and explicit media queries in 25 of them. Responsive work therefore exists, but it is page-specific and incomplete rather than governed by a shared system.

The local `run.sh` build was blocked by unrelated work in `JobRunRetentionTests.cs`, which calls a missing `EfJobRunRepository.TrimToLatestAsync`. The audit deliberately did not alter that work. The last successful Host build was run with `--no-build` instead.

## Current strengths

### Shell and navigation

- The 236px desktop sidebar is compact, stable, and understandable.
- Mobile navigation already becomes an off-canvas drawer with a backdrop and close action.
- The command-search affordance and keyboard hint are strong enterprise/productivity patterns.
- Workspace, project, user, notification, and run-status context already have places in the shell.
- Dark and light token sets already exist.

### Operational UI

- Status colours and tabular numerals make run state scannable.
- Dashboard stat cards form a coherent summary strip on desktop.
- Chat correctly becomes a focused full-height mobile experience with its composer anchored at the bottom.
- Artifacts stacks its controls cleanly on mobile.
- Observability, project overview, onboarding, and About reflow without document-level horizontal scrolling.
- Tables and canvases already use contained overflow in several places.

### General visual language

- Inter and JetBrains Mono are appropriate for a technical enterprise application.
- The current interface is restrained rather than decorative.
- Borders and surfaces provide enough structure without excessive shadows.
- Core controls are visually consistent enough to evolve rather than replace wholesale.

## Problems preventing a mature enterprise result

### 1. Responsive correctness is inconsistent

#### Wiki is a mobile blocker

The Wiki retains its desktop documentation rail and article side by side at 390px. The article starts off-screen, the page has internal horizontal overflow, and at least 40 visible descendants extend beyond the viewport.

Required behaviour:

- hide the documentation rail below tablet width;
- replace it with a `Documentation` drawer or section selector;
- render the article at the full mobile width;
- allow only code blocks and genuinely tabular content to scroll horizontally.

#### Dashboard jobs grid is technically scrollable, not mobile-usable

At 390px, `.jobs-scroll` has a 364px client width and 952px scroll width. The header, project columns, and empty-state sentence are visibly clipped with no strong cue that the region scrolls.

Required behaviour:

- desktop/tablet: retain a real data table with sticky headers and column controls;
- mobile: render each job as a compact record card or definition list;
- keep status, job name, project, start time, duration, and primary action visible;
- move secondary fields into disclosure;
- never render an 880px minimum-width empty state.

#### Touch sizing is not systematic

The audit found visible interactive controls below a 40px dimension on every audited mobile page. Small icon buttons, filter pills, tabs, and documentation links are the main sources.

Required behaviour:

- use a 44 × 44px minimum hit area for primary touch interactions;
- compact visuals may remain smaller if a pseudo-element or wrapper provides the hit area;
- preserve the denser desktop control size as a separate density mode.

#### Mobile information prioritisation needs refinement

- Four dashboard stat cards stack as four large rows, consuming most of the first screen. Use a 2 × 2 compact KPI grid at phone widths where labels fit.
- Topbar subtitles are repeatedly truncated. On phones, keep the product/page name in the topbar and move context/subtitle into the page body.
- Long onboarding copy reflows correctly but is too dense for a task flow. Convert it into a stepper with one current step and expandable supporting detail.
- Drawers, modals, editors, tables, and canvases need explicit mobile modes rather than only width reductions.

### 2. The product looks like a developer tool, not yet a broad enterprise platform

The near-black background, terminal-like green primary colour, very small metadata, and large empty expanses communicate an engineering console. That is appropriate for jobs and observability, but less appropriate for planning, CRM, council research, application preparation, access management, and executive review.

Recommended visual adjustment:

- make the light theme the business-facing default while retaining dark mode;
- use cool neutral/slate surfaces and a darker navy text hierarchy;
- retain the tenant accent for primary actions and selection, but separate `primary` from `success`—both currently resolve to the same green;
- raise standard body text to 14px and reserve 11–12px for secondary metadata;
- avoid 9–10px text for information users must read;
- standardise radii rather than mixing global 12px cards with page-level 8px cards;
- use shadows only for overlays, drawers, popovers, and floating commands; use borders/surface contrast for ordinary panels.

### 3. Page structure is not yet systematic

Pages use similar ideas but implement their own page headers, toolbar layouts, filters, cards, empty states, and responsive overrides. This causes visual drift and multiplies mobile fixes.

Create shared primitives rather than another styling pass over individual pages:

- `PcPageHeader` — breadcrumb/context, title, description, status, primary and secondary actions;
- `PcToolbar` — search, filters, view options, saved views, bulk actions;
- `PcDataTable` — desktop table plus a required mobile record renderer;
- `PcStatTile` — compact and comfortable density variants;
- `PcEmptyState` — explanation, primary next step, optional documentation link;
- `PcStatusBadge` — semantic states independent of tenant accent;
- `PcRecordHeader` — identity, lifecycle state, owner/council/project and actions;
- `PcDrawer` / `PcBottomSheet` — desktop side panel and mobile full-screen/bottom-sheet modes;
- `PcFormSection` — title, rationale/help, fields, validation summary;
- `PcResponsiveTabs` — tabs on desktop, menu/select or scrollable tabs on mobile;
- `PcSplitWorkspace` — resizable desktop panes that become tabs on mobile;
- `PcCommandBar` — preserve the existing search concept while adding actions and recent items.

These components should consume tokens from a dedicated design-system stylesheet. The large inline token/primitives block in `App.razor` should be extracted so theme values and components can be tested and versioned independently.

### 4. Empty states are passive

Several pages state that no data exists but provide no direct recovery or onboarding action.

Examples:

- Dashboard: “No runs yet” should offer `Create job`, `Run sample`, or `Open onboarding` based on permission and project state.
- Artifacts: link directly to jobs and explain artifact production.
- Observability: offer a job action and explain what will appear.
- Projects: provide a concrete onboarding action rather than referring only to an MCP tool.

An enterprise empty state should answer:

1. What is this area?
2. Why is it empty?
3. What can the user do next?
4. Is the empty state expected, filtered, or an error?

### 5. Enterprise data workflows need stronger conventions

For Jobs, Chains, Artifacts, CRM, entities, development applications, and administration:

- persistent filter/search toolbar;
- visible result count and active-filter summary;
- sortable columns and saved views;
- pagination or virtualisation with clear data bounds;
- column visibility and density controls on desktop;
- row selection and batch actions only when a real batch use case exists;
- semantic status with icon + text, never colour alone;
- clear loading, empty, stale, permission-denied, partial-data, and error states;
- row/detail navigation with a stable record header;
- audit/activity history for administrative changes;
- destructive actions grouped away from primary actions and confirmed with specific consequences.

## Benchmark synthesis

### IBM Carbon

Useful patterns:

- productive and compact density for data-heavy applications;
- table toolbar, sorting, expansion, batch action, pagination, and accessibility as one component family;
- strict shell and spacing rules;
- accessibility guidance is part of component definition, not a separate polish phase.

Adopt the discipline and anatomy, not IBM branding.

Reference: <https://carbondesignsystem.com/components/data-table/usage/>

### PatternFly

Useful patterns:

- operational admin console focus;
- strong page, toolbar, drawer, wizard, empty-state, and table patterns;
- responsive tables distinguish columns that can hide, collapse, wrap, or become compound rows;
- clear separation of global navigation, local navigation, content, and contextual actions.

Reference: <https://www.patternfly.org/components/table/design-guidelines/>

### Linear

Useful patterns:

- command-first navigation and keyboard efficiency;
- compact side navigation;
- restrained colour and low visual noise;
- predictable overlays and quick actions.

Keep PlaceContext search and compact navigation, but do not copy Linear's issue-tracker information architecture.

### Sentry

Useful patterns:

- status and exception-first operational overview;
- dense but scannable filter bars;
- issue/run detail with chronology and evidence;
- progressive disclosure of technical metadata.

Apply this most heavily to Jobs and Observability.

### Stripe Dashboard

Useful patterns:

- readable forms with explicit labels and contextual help;
- conservative radius and polished focus/error/loading states;
- clear relationship between summary, details, and primary action;
- dense data inside generous page chrome.

Apply this most heavily to settings, application preparation, and billing-like administrative forms.

### Salesforce Lightning

Useful patterns:

- record pages that keep identity, status, ownership, and actions stable;
- lifecycle/progress path for long-running business processes;
- activity timeline, related records, and contextual panels;
- role-appropriate administrative discoverability.

Apply this to CRM and the future Development Application preparation workspace without inheriting Salesforce's visual complexity.

## Responsive contract

Use explicit layout bands:

| Band | Width | Contract |
|---|---:|---|
| Phone | `< 640px` | One content column; drawer navigation; 44px targets; table-to-card transformation; panels full-screen; no persistent secondary rail |
| Tablet | `640–1023px` | One or two columns; compact drawer/rail; tables hide secondary columns; split workspaces may become tabs |
| Desktop | `1024–1439px` | Persistent navigation; productive-density tables; two-pane workspaces where useful |
| Wide | `≥ 1440px` | Constrained readable content or expanded data canvas; never stretch prose merely to fill width |

Core rules:

- no page-level horizontal scrolling at 320px CSS width;
- horizontal scrolling is permitted only for code, canvases, or explicitly identified data regions;
- scrolling regions must expose a visual cue and keyboard access;
- use `100dvh` for full-height mobile workspaces with a `100vh` fallback;
- account for safe-area insets on fixed composers and bottom actions;
- side panels become full-screen drawers or bottom sheets on phones;
- multi-column forms collapse to one column while preserving logical and tab order;
- desktop hover behaviour must have visible touch and keyboard equivalents;
- the UI must remain usable at 200% browser zoom;
- remove `maximum-scale=1` and `user-scalable=no` from the viewport meta tag so users can zoom.

## Page-specific target states

### Dashboard

Desktop:

- exception-first summary (`Needs attention`, `Running`, `Queued`, `Completed`);
- compact trends or deltas only when data exists;
- recent work table with strong status and row actions;
- contextual primary action based on project state.

Mobile:

- 2 × 2 compact KPI tiles;
- recent jobs as record cards;
- sticky or clearly visible primary action only if it does not obscure content.

### Jobs and Observability

Desktop:

- PatternFly/Carbon-style toolbar and productive table;
- saved filters, visible status semantics, duration and owner/project context;
- detail drawer for quick inspection, full page for deep diagnostics.

Mobile:

- status-led cards;
- filters in a sheet;
- high-value actions visible, secondary actions in an overflow menu;
- chronological diagnostics rather than a compressed desktop grid.

### Job chains and canvas

Desktop:

- canvas as the primary workspace with inspector side panel;
- minimap/zoom/fit controls and validation summary;
- stage selection and execution status are distinct.

Mobile:

- stage list/pipeline is the default representation;
- select a stage to open a full-screen editor;
- canvas may remain available as an optional pan/zoom view, not the only editor.

### CRM and Development Applications

Desktop:

- Salesforce-inspired record workspace: stable record header, process path, overview, documents, tasks, correspondence, evidence, and audit timeline;
- council and assessment pathway are visible record context;
- complex forms use sections and a completion summary.

Mobile:

- process status, next required action, due dates, and blockers first;
- sections become an accordion or step list;
- document upload supports camera/files and clear progress;
- secondary reference data moves behind disclosure.

### Wiki and documentation

Desktop:

- retain the documentation rail and readable article width.

Mobile:

- replace the persistent rail with a drawer/section selector;
- full-width article;
- sticky article title is optional, but must not consume excessive vertical space.

### Settings and administration

Desktop:

- grouped settings with section navigation and a consistent save bar;
- show effective/inherited values and permission requirements.

Mobile:

- section navigation drawer;
- one-column forms;
- sticky bottom save area only when there are unsaved changes.

## Delivery plan

### Slice 1 — Responsive correctness and accessibility

1. Remove zoom-disabling viewport directives.
2. Fix Wiki mobile navigation and article width.
3. Replace the dashboard mobile jobs grid with record cards.
4. Establish 44px mobile target areas.
5. standardise `100dvh`, safe-area, overflow, and topbar behaviour.
6. Add viewport smoke tests and screenshots.

This slice should not change product colour or branding.

### Slice 2 — Design-system foundation

1. Extract tokens and global primitives from `App.razor`.
2. Define colour roles independently: accent, primary, success, warning, danger, info, neutral.
3. Define typography, spacing, radii, border, elevation, focus, motion, and density tokens.
4. Implement `PcPageHeader`, `PcToolbar`, `PcEmptyState`, `PcStatusBadge`, `PcStatTile`, and responsive control sizing.
5. Migrate Dashboard first as the reference implementation.

### Slice 3 — Enterprise data surfaces

1. Implement `PcDataTable` with a required mobile renderer.
2. Migrate Jobs, Artifacts, Observability, data entities, and project data.
3. Add filters, saved views where demonstrated, column controls, pagination, and consistent states.
4. Preserve dense desktop operation while making mobile task-focused.

### Slice 4 — Complex workspaces

1. Implement `PcSplitWorkspace`, responsive drawer/bottom sheet, and responsive tabs.
2. Migrate Chat, editors, chain canvas, CRM, and Development Application workspaces.
3. Use process steps and record headers for business workflows.

### Slice 5 — Visual refinement

1. Make the light business theme the recommended default while retaining dark mode.
2. Refine neutral palette, typography, empty-state illustrations/icons, and motion.
3. Run cross-theme contrast checks and visual-regression review.
4. Avoid redesigning functional flows without evidence from usability testing.

## Acceptance matrix

Every migrated page must be exercised at least at:

- 320 × 568 — minimum supported phone;
- 390 × 844 — common phone;
- 768 × 1024 — tablet portrait;
- 1024 × 768 — tablet landscape/small desktop;
- 1440 × 900 — desktop;
- 1920 × 1080 — wide desktop;
- 200% zoom at a 1280px browser width;
- dark and light themes.

Automated checks:

- no unexpected document-level horizontal overflow;
- no console or uncaught page errors;
- primary workflows are keyboard reachable;
- visible focus state on every interactive control;
- touch targets meet the mobile target contract;
- modal/drawer focus is trapped and restored;
- screenshots for reference pages at phone, tablet, and desktop;
- loading, empty, populated, error, permission-denied, and long-content states.

Manual checks:

- navigation with mouse, keyboard, and touch;
- long names, long council/application identifiers, and localisation-safe wrapping;
- data tables with many columns and rows;
- editors with software keyboard present;
- file upload and download on phone;
- screen-reader landmarks, headings, labels, statuses, and validation summaries.

## Immediate recommendation

Start with Slice 1, then use Dashboard as the design-system reference page in Slice 2. Do not begin with a broad colour/typography reskin: it would make screenshots look newer while leaving the Wiki, tables, touch targets, viewport zoom, and complex workspaces structurally broken on mobile.
