# Workspace component styles

Agent Store and Data profiles use a local, prefixed subset of **daisyUI 5.7.50**.
The portal serves the checked-in third-party CSS asset; running or building the .NET
application does not require Node.js, a CDN or a browser CSS compiler.

To update the vendored component bundle from this directory:

```sh
npm ci
npm run build
```

`components.css` is the complete component allowlist. It disables automatic scanning,
Tailwind's reset, global themes and unrelated utilities. All component classes start
with `dui-`; theme variables are supplied inside `.product-workspace` by `app.css`.
This prevents the library from styling other portal pages. The only reset is local
to workspace form controls in a lower CSS layer. `build.mjs` renames daisyUI's
`--border` width variable to `--dui-border-width` because the portal already uses
`--border` for a color; it also copies the upstream licenses alongside the CSS.

Keep the exact package versions and lockfile together. When upgrading, update the
license notices in `wwwroot/vendor/workspace-ui-LICENSES.txt` and
`THIRD-PARTY-NOTICES.md`, rebuild the CSS, and review both themes at desktop and mobile
sizes. Page-specific CSS should handle layout and chart sizing; buttons, fields,
cards, tabs, badges and stats should use the library's component rules.
