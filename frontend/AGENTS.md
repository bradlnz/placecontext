# PlaceContext React frontend

Follow the [Vercel React best-practices guide](https://github.com/vercel-labs/agent-skills/blob/main/skills/react-best-practices/AGENTS.md) for all React work in this directory.

## Architecture

- Organise product code as `src/domains/<domain>/sections/<section>`. Keep section-only components beside their section.
- Put genuinely cross-domain infrastructure and primitives in `src/shared`; do not create barrel `index.ts` files.
- Keep the composition root in `src/app`. Dependencies flow from `app` to domains to shared code, never back toward `app`.
- A migration slice owns its API adapter, runtime schema, model types, events, page, components, and tests.
- Browser code calls the canonical versioned `/api/v1` PlaceContext API. Endpoints use the authentication policy appropriate to their consumers; do not create a separate frontend-only API namespace.
- Blazor and React coexist only during the incremental migration. React lives at `/app` for now; links to unmigrated pages use the legacy navigation adapter. React must ultimately own every route in `src/app/host-route-catalog.ts` and replace Blazor.
- Treat the `PlaceContext.Host` Blazor UI as the canonical visual and behavioural specification. Port each slice faithfully; the migration is not a redesign.

## Async and events

- Treat all I/O and user commands as asynchronous, accepting an `AbortSignal` where cancellation applies.
- Start independent requests together and await them with `Promise.all`.
- Prefer one composed API read contract when a page would otherwise create a client/server request waterfall.
- Use TanStack Query for remote/server state. Do not mirror query data into component state.
- Put interaction logic in async event handlers. Use the typed event bus for cross-domain outcomes and commands; keep local visual state local.
- Event subscribers return promises and publishers await all subscribers. Event names are past-tense outcomes or explicit requests.
- Effects subscribe to external systems only. Do not use effects to derive state or model user actions.

## React and TypeScript

- Keep TypeScript strict and validate untrusted API data at runtime.
- Route-level code split every migrated section. Use Suspense boundaries with layout-shaped fallbacks.
- Import modules directly from their source files; do not introduce barrel files.
- Define components at module scope, derive values during render, use functional state updates, and prefer explicit conditionals.
- Preserve accessible HTML, visible keyboard focus, reduced-motion support, and responsive layouts.
- Add or update tests with each migrated slice. `npm run lint`, `npm test`, and `npm run build` must pass.
