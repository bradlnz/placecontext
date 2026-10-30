# PlaceContext icons

UI icons use the official [Lucide](https://lucide.dev/) SVG source files, pinned to
version **1.65.0**, commit `81875c8c42d2320fa5123b37fee3554f4ca2bddc`.
The app serves a small, local SVG sprite. Icons do not request a CDN or load a
runtime JavaScript library.

## Use an icon

```razor
<AppIcon Kind="phone" />
<AppIcon Kind="confidence" Size="20" />
<button aria-label="Refresh evidence"><AppIcon Kind="refresh" /></button>
```

`Kind` accepts the existing semantic aliases and the canonical Lucide names in
the bundled subset. The renderer uses Lucide's 24-unit view box and two-unit
stroke, with the surrounding text color. The default size is 16 pixels.
Icons are decorative and hidden from assistive technology; the containing
control must have visible text or an accessible label. Status meaning must also
remain in text. Keep tenant branding and data visualizations separate from UI
icons.

## Add or update icons

Edit `lucide.json`, choosing canonical names from the pinned upstream source.
Then run from the repository root (Python 3, network access required):

```sh
python3 tools/icons/vendor_lucide.py
python3 tools/icons/vendor_lucide.py --check
```

The script validates SVG geometry, builds the sprite, and updates the generated
`MainLayoutIconCatalog.cs`. It saves the complete upstream license and a manifest
with the pinned source commit and SHA-256 of every source icon, sprite and license.
Review and commit those files together. Never add hand-drawn paths to the
catalogue or replace the license with a shortened attribution.

`LucideIconCatalogTests` verifies that literal UI icon names and configured aliases
resolve to a bundled symbol, that assets match the manifest, and that unknown
names cannot inject markup. A browser check should confirm the local sprite
loads and icons remain visible in light and dark themes.
