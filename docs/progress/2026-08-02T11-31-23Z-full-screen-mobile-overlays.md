# Full-screen mobile overlays and scroll isolation

**Completed:** 2026-08-02 21:31 AEST

## Change

- Made shared dialogs and slide-out editors occupy the full mobile viewport below 700px.
- Kept modal headers and footers fixed around a separately scrolling body.
- Increased header close controls to a 44 × 44px touch target and respected safe-area insets.
- Locked document scrolling whenever the focus-layer manager opens a modal, drawer, or mobile side panel.

## Verification

- Added regression contracts for full-viewport overlays, close-target sizing, and background scroll locking.
- Focused tests passed.
- Full `PlaceContext.Host.Tests` suite passed: 114 tests, 0 failures.
- Browser rendering at an emulated 390 × 844 viewport confirmed a 390 × 844 modal, zero overlay padding, zero border radius, a 44 × 44 close target, and `overflow: hidden` on both `html` and `body` while the focus layer is active.
