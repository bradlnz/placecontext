# Saturday progress — 2026-08-01

## Delivered

- The existing portal security/UI feature commit (`ee04f04`) is on `main` through PR #12 (`a9d2840`).
- Added shared modal and side-pane focus isolation (`2448636`):
  - selects the topmost open layer and supports nested modals;
  - moves focus into the layer, traps Tab navigation, and restores the opener on close;
  - makes the underlying page inert to pointer, keyboard, and assistive-technology focus;
  - contains overscroll and uses dynamic mobile viewport heights;
  - gives the mobile main navigation and Chat workspace panes close controls and dismissible scrims.
- Merged the focus work into `main` as `009c20a` and pushed it to `origin/main`.

## Verification

- `dotnet build PlaceContext.slnx --no-restore`: passed with 0 errors and 8 pre-existing warnings.
- `dotnet test PlaceContext.slnx --no-build`: 868 passed, 6 skipped, 0 failed.
- `dotnet test tests/PlaceContext.Host.Tests/PlaceContext.Host.Tests.csproj --no-build`: 104 passed, 0 failed.
- `node --check src/PlaceContext.Host/wwwroot/placecontext.js`: passed.
- Mobile Chromium smoke test at 390 × 844: passed background inertness, Tab trapping, nested-modal focus restoration, and side-pane scrim checks.

## Deployment

- Ran `./deploy.sh` successfully from merged `main`.
- Pushed image digest `sha256:ee34ea3de7b33d99628046f02e9cf3cf4b97b6d7a4d567d4e965248f8e2f870f`.
- Restarted `deployment/placecontext`; replacement pod `placecontext-58d8cf4876-sxpgg` reported `2/2 Running` with 0 restarts.

## Follow-up notes

- Kubernetes warned that `PlaceContext__PublicBaseUrl` is defined twice in the deployment environment; the later value currently hides the earlier definition.
- Build warnings remain for xUnit analyzer style, mixed EF Core Relational versions (10.0.4/10.0.9), and one nullable dereference in `ChatViewModel.Formatting.cs`.
- The earlier untracked `progress.md` was deliberately left untouched.
