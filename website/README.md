# PlaceContext homepage and documentation

A standalone, static product website for PlaceContext. The homepage presents an agentic data platform: connected data, agents, jobs, workflows, and human decisions. Documentation is authored in `content/` and rendered as individual pages with local search.

## Develop

Use Node.js 22 or newer. Install `marked` with `npm install`, then:

```sh
npm run build
npm run check
```

Serve `dist/` with a static server. Every guide has its own `index.html`. The output needs no server runtime or external APIs. Search runs in the visitor's browser from `search-index.json`.

CSS and client behavior are in `assets/site.css` and `assets/site.js`; the build copies these assets into `dist/assets/`. The reusable layout, homepage content, and documentation metadata are in `scripts/build.mjs`.

## Product evidence

Content was reviewed against PlaceContext main at `4efae30508d17f30ff4979d09b3402391328d926` on 11 November 2026. Current source was retrieved through the authenticated GitHub connection and source text checked against Git blob hashes.

The corgi logo is the existing README brand asset, based on the application corgi outline. The four walkthrough screenshots are copied byte-for-byte from that revision's `docs/images/`, checked against Git blob hashes. They show sample customer data and an external-agent response in the real application. They do not demonstrate automatic model execution. See the repository's `docs/images/workflow-screenshots.md` for original capture provenance.

The site uses PlaceContext's own positioning. It contains no competitor parity assessment, invented customer endorsements, usage metrics, pricing, or managed-service signup promises.

## Deploy

Set `SITE_ORIGIN` to the intended website origin before building so canonical links and the sitemap match the deployment. Publish the generated `dist/` directory on a static host. Do not route the authenticated PlaceContext application through this website.

The website source is independent of the .NET application and does not change its runtime routes.
