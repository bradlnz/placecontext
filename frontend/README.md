# PlaceContext React migration

The React portal is migrating every `PlaceContext.Host` section one vertical slice at a time. Each slice replicates the corresponding Blazor UI rather than redesigning it, and the completed React application will replace the Blazor frontend. During the side-by-side phase React is mounted at `/app`; the first completed slice is Workspace Overview at `/app/overview`.

```bash
cd frontend
npm install
npm run dev       # http://localhost:5173/app/overview; proxies API/auth to :7700
npm run lint
npm test
npm run build     # emits fingerprinted assets into the Host wwwroot/app directory
```

Start the .NET host on port 7700 before using the Vite development server. Authentication remains the host's HTTP-only cookie flow; the frontend never stores credentials.

Each domain is split further by UI section:

`src/app/host-route-catalog.ts` is the checked migration contract for all 40 Host routes. A route changes from `planned` to `migrated` only when its React UI, async API/event integration, UI tests, and Host-fidelity check are complete.

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
