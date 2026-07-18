# Feasibility Reports — Shopify theme

A standalone Online Store 2.0 starter theme for selling **Feasibility Reports**
(property / site-analysis documents) as a digital service. It lives in this
`shopify/` folder only — nothing else in the repository references it, and it
is not part of the .NET build.

The PlaceContext platform produces the report; Shopify handles the storefront,
checkout and payment. After an order is paid, the report is produced manually
via the platform and emailed to the customer (see
[Operational flow](#operational-flow-after-an-order-is-paid)).

## What's in the theme

```
shopify/
├── assets/
│   ├── theme.css                  # Self-contained styles (no framework, no build step)
│   └── theme.js                   # Progressive enhancement (mobile nav, FAQ accordion, submit state)
├── config/
│   └── settings_schema.json       # Theme settings: colors, support email
├── layout/
│   └── theme.liquid               # <html> shell: header, content_for_layout, footer
├── locales/
│   └── en.default.json            # All UI strings
├── sections/
│   ├── header.liquid              # Sticky header: logo/name, menu, cart count
│   ├── footer.liquid              # Footer: tagline, menu, support email
│   ├── hero.liquid                # Landing hero (headline, CTA, optional background image)
│   ├── how-it-works.liquid        # 3-step process (blocks, with built-in defaults)
│   ├── sample-report.liquid       # Sample report preview + PDF download link
│   ├── faq.liquid                 # FAQ accordion (native <details>, works without JS)
│   ├── main-product.liquid        # Feasibility Report product form (see below)
│   └── main-page.liquid           # Generic page content
├── snippets/
│   └── icon.liquid                # Inline SVG icons
└── templates/
    ├── index.liquid               # Home: hero + how-it-works + sample-report + faq
    ├── product.liquid             # Product: main-product + sample-report + faq
    ├── page.liquid                # Static pages (policies, about, contact…)
    ├── cart.liquid                # Cart incl. the captured site-details properties
    └── 404.liquid
```

### The product template

`sections/main-product.liquid` is tailored to the Feasibility Report product:

- **What's included** list (editable as blocks in the theme editor; ships with defaults).
- **Delivery-timeframe callout** next to the product media (editable text).
- **Site-details fields**, captured as *line item properties* so they travel with
  the order and are visible in Shopify admin (no app required):
  - `properties[Site address]` — required text field
  - `properties[Site description and notes]` — optional textarea
  - `properties[Document link]` — optional URL field for a share link
    (Dropbox/Drive/OneDrive); the help text also tells buyers they can email
    documents to the support address after checkout. Shopify line item
    properties cannot accept file uploads directly, hence the link/email route.
- A **sample-report preview** section is included in `templates/product.liquid`
  (same shared section as on the home page — edit it once).

If you add variants to the product (e.g. "Standard — 5 business days" and
"Express — 2 business days" at different prices), the template renders a
variant dropdown automatically.

## Install

You need a Shopify store (any plan). Both install methods upload the theme as
**unpublished** — you preview it, then publish when ready.

### Option A — Shopify CLI (recommended for iterating)

1. Install the CLI (requires Node 18+):

   ```sh
   npm install -g @shopify/cli@latest
   ```

2. From this directory:

   ```sh
   cd shopify
   shopify theme push --unpublished --store YOUR-STORE.myshopify.com
   ```

   The first run opens a browser to authenticate. Follow the printed link to
   preview the theme in your store admin.

3. Optional while working on the theme: `shopify theme dev` gives a live-reload
   preview, and `shopify theme check` lints the theme.

### Option B — Zip and upload via the admin

1. Zip only the theme directories (README.md and dotfiles are not theme files):

   ```sh
   cd shopify
   zip -r ../feasibility-theme.zip assets config layout locales sections snippets templates
   ```

2. In Shopify admin: **Online Store → Themes → Add theme → Upload zip file**,
   choose `feasibility-theme.zip`.

3. Click **Customize** to configure, then **Publish** when ready.

## Set up the store content

1. **Store name / brand** — Settings → General → Store details. The theme shows
   `shop.name` in the header and footer unless you upload a logo
   (Customize → Header → Logo image).
2. **Create the product** — Products → Add product:
   - Title: e.g. *Feasibility Report*. Description: what the buyer gets.
   - Price: your report price.
   - Media: upload a cover image (a placeholder card is shown until you do).
   - Shipping: **uncheck "This is a physical product"**.
   - Inventory: uncheck "Track quantity" (digital service).
   - Status: Active; make sure it is available on the **Online Store** channel.
   - No template assignment needed — the default product template already
     contains the site-address / notes / document-link fields.
   - Optional: add variants for delivery speed (e.g. Standard / Express).
3. **Menus** — Online Store → Navigation. The header uses the `main-menu`
   handle, the footer the `footer` handle. Useful entries: Home (`/`),
   Order (your product URL), and anchors `/#how-it-works`, `/#sample-report`,
   `/#faq`.
4. **Sample report PDF** — Settings → Files → upload your sample PDF, copy its
   URL, then Customize → open the **Sample report** section → paste the URL into
   "Sample PDF URL" and upload a cover image. Until then the section shows a
   placeholder card and hides the download button.
5. **Theme settings** — Customize → Theme settings:
   - *Colors*: accent/background/text.
   - *Store details → Support email*: shown in the footer and in the product
     page's document hand-off hint. **Replace the default `reports@example.com`.**
6. **Copy** — hero headline, steps, FAQ answers and the "What's included" /
   delivery texts are all editable in the theme editor (sections & blocks).
   Sections render sensible defaults until you add blocks.
7. **Policies & pages** — Settings → Policies (refund, privacy, terms) and
   Online Store → Pages (about, contact) use the generic page template; add
   them to the footer menu.
8. **Payments / checkout** — Settings → Payments. Consider editing the order
   confirmation email (Settings → Notifications) to restate the delivery
   window for digital orders.

## Operational flow after an order is paid

Delivery is **manual** — the platform produces the report, you send the link:

1. **Order arrives.** Shopify emails you (or check Orders in admin). Open the
   order; under the product title you'll see the line item properties:
   **Site address**, **Site description and notes**, and optionally
   **Document link**.
2. **Collect inputs.** If the buyer supplied a document link, download the
   files. If anything is missing (unclear address, no access info), email the
   buyer now — the delivery window should start once inputs are complete.
3. **Run the platform.** In the PlaceContext portal, start the report job chain
   for the order's site details (site address + description; attach any
   documents from step 2 as inputs).
4. **Verify the artifact.** When the chain completes, open the report artifact
   and sanity-check it (correct site, findings present, PDF renders).
5. **Deliver.** Copy the report artifact's delivery link and email it to the
   customer. Suggested template:

   > Subject: Your Feasibility Report is ready (order {{ order.name }})
   >
   > Hi {{ customer.first_name }},
   >
   > your feasibility report for {{ site address }} is ready:
   > **{{ report delivery link }}**
   >
   > If anything looks off or you have questions, just reply to this email.

6. **Close out in Shopify.** Open the order → **Mark as fulfilled** (no
   tracking needed; you can paste the delivery link into the fulfillment note
   so it appears in Shopify's shipping email too). Add an order note linking
   the platform job/run ID for traceability.

Optional later: a digital-delivery app (e.g. Shopify's free *Digital
Downloads* app) or a small Shopify Flow / webhook integration can automate
steps 5–6. Billing, tax and refunds stay in Shopify either way.

## Development notes

- No build step: plain CSS (`assets/theme.css`) and vanilla JS
  (`assets/theme.js`). Edit, then `shopify theme push` (or re-zip) again.
- Lint with `shopify theme check`.
- All user-facing strings live in `locales/en.default.json`.
- Scope: this starter intentionally ships only the templates listed above.
  Routes like `/collections/*`, `/search`, `/blogs/*` and customer accounts
  have no templates yet — add them if the store grows beyond a one-product
  funnel.

## Placeholders to replace before launch

- Store/brand name (Settings → General) and logo (theme editor → Header)
- Product title, description, **price** (and optional variants)
- Product cover image; sample-report **PDF + cover image**
- **Support email** in Theme settings (default `reports@example.com`)
- Hero copy and CTA links; FAQ answers; delivery-timeframe text
- Policy pages (refund / privacy / terms) and footer menu
- `config/settings_schema.json` → `theme_documentation_url` /
  `theme_support_url` (currently `https://example.com/…`)
