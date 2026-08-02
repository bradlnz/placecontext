# CRM client notes overflow fix

**Completed:** 2026-08-02 21:13 AEST

## Change

- Made client notes wrap long unbroken content within the client detail panel by applying `overflow-wrap: anywhere` to `.client-notes`.
- Added a host UI contract regression test that requires this wrapping behavior.

## Root cause

The notes renderer preserved whitespace with `white-space: pre-wrap` but did not provide a break opportunity for long unbroken strings such as URLs or generated identifiers, allowing their min-content width to exceed the client detail panel.

## Verification

- Confirmed the focused regression test failed before the CSS change.
- Focused test passed in `mcr.microsoft.com/dotnet/sdk:10.0`.
- Full `PlaceContext.Host.Tests` suite passed in `mcr.microsoft.com/dotnet/sdk:10.0`.
