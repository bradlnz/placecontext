# PlaceContext React frontend

The React portal is replacing every `PlaceContext.Host` section one vertical slice at a time. Each
slice replicates the corresponding Blazor UI rather than redesigning it. `PlaceContext.App` is the
permanent web edge and serves the React application under `/app`; `PlaceContext.Host` exists only
while its remaining routes and browser API contracts are migrated.

```bash
cd frontend
npm install
npm run dev       # http://localhost:5173/app/overview; proxies API/auth to :7700
npm run lint
npm test
npm run build     # emits fingerprinted assets into PlaceContext.App/wwwroot/app
```

Start `PlaceContext.App` on port 7700 before using the Vite development server. The remaining
identity/session endpoints must move from the legacy Host before the React application can run
without that compatibility process; the frontend continues to use HTTP-only cookies and never
stores credentials.

Each domain is split further by UI section:

`src/app/host-route-catalog.ts` is the checked migration contract for all 40 legacy routes. A route
changes from `planned` to `migrated` only when its React UI, async API/event integration, UI tests,
and fidelity check are complete.

## PlaceContext API

React calls the canonical `/api/v1` PlaceContext API. Browser endpoints use cookie authentication while machine endpoints retain their existing token/API-key policies; there is no separate frontend-only API namespace. Every response is runtime-validated at the React boundary.

```text
src/
  app/                              composition, providers, routes
  domains/
    navigation/sections/app-shell/ shared application shell
    workspace/
      api/                           transport and runtime validation
      events/                        typed domain event contract
      model/                         domain types
      sections/overview/             first migrated vertical slice
  shared/                            cross-domain API, events, UI, navigation
```
