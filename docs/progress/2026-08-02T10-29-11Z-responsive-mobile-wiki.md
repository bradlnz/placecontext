# Progress — Responsive mobile Wiki navigation

Timestamp: 2026-08-02T10:29:11Z

## Completed

- Replaced the fixed mobile Wiki contents column with an accessible disclosure control.
- Added `aria-expanded`, `aria-controls`, article navigation labelling and active-page semantics.
- Closes the contents drawer after selecting an article or changing routes.
- Added 44px touch targets, bounded mobile scrolling and wrapping safeguards for long article content.
- Preserved the existing desktop two-column documentation layout.

## Verification

- `ResponsiveShellContractTests`: 2 passed, zero failed.
- The Host test build completed with only the existing EF Core version warnings.
- `git diff --check` passed.
