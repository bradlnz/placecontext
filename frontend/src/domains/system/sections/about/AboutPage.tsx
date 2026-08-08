export function AboutPage() {
  const copyrightYear = new Date().getUTCFullYear()

  return (
    <div className="about-page">
      <title>PlaceContext — About</title>
      <article className="dccard about-card">
        <div className="about-brand-row">
          <div className="about-logo" aria-hidden="true">
            <svg viewBox="0 0 24 24" fill="none" stroke="#fff" strokeWidth="2.1" strokeLinecap="round" strokeLinejoin="round"><path d="M5 8l3.5 4L5 16" /><path d="M13 16h6" /></svg>
          </div>
          <div><div className="about-product">PlaceContext</div><div className="about-tagline">a full-scale data platform</div></div>
        </div>

        <p className="about-lead">PlaceContext is a self-hosted data platform for ingesting, modelling, processing, governing, and activating operational data. It brings project databases, entity relationships, containerised workloads, analytics, observability, artifacts, and CRM workflows into one platform.</p>

        <section className="about-section">
          <h1>Platform</h1>
          <p>Build project-scoped tables and entity models, run scheduled or event-driven jobs and pipelines, inspect lineage and execution traces, create reports and charts, and move operational records through CRM workflows. Use the web workspace directly or connect automation and AI clients through MCP. Agent context remains supported, but it is one capability of the wider data platform.</p>
        </section>
        <section className="about-section">
          <h1>Product</h1>
          <p>PlaceContext is open-source software by <strong>Bradley Lietz</strong> (CTRL SIGNAL SOFTWARE PTY LTD), released under the MIT License.</p>
        </section>
        <section className="about-section">
          <h1>Your data &amp; jobs</h1>
          <p><strong>You own your data and your jobs.</strong> Project data, decisions, activity, secrets, job definitions, runs, and artifacts remain yours. We claim no ownership of content you create or import — the platform only stores and runs it for you.</p>
        </section>
        <section className="about-section about-built-on">
          <h1>Built on</h1>
          <p>ASP.NET Core &amp; EF Core (MIT) · PostgreSQL (PostgreSQL License) · ModelContextProtocol SDK (MIT) · k3s/k3d (Apache-2.0) · MinIO (AGPL-3.0, unmodified service) · Bubble Tea &amp; Lip Gloss (MIT) · Monaco Editor (MIT) · Markdig (BSD-2-Clause) · Cronos (MIT) · Npgsql (PostgreSQL License) · Geist typeface (SIL OFL 1.1). Full attributions ship in <code>THIRD-PARTY-NOTICES.md</code>.</p>
        </section>
        <footer className="about-footer">Created by <strong>Bradley Lietz</strong>.<br /><span>© {copyrightYear} Bradley Lietz / CTRL SIGNAL SOFTWARE PTY LTD — product rights reserved; your data &amp; jobs are yours.</span></footer>
      </article>
    </div>
  )
}
