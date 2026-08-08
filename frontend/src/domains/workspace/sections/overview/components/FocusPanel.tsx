import type { WorkspaceFocus } from '../../../model/workspace'

interface FocusPanelProps {
  focus: WorkspaceFocus
}

export function FocusPanel({ focus }: FocusPanelProps) {
  const hasItems = focus.items.length > 0

  return (
    <section className="dccard focus-card" aria-labelledby="focus-title">
      <div className="focus-head">
        <span className="section-title" id="focus-title">Current focus</span>
        <span className="section-hint">what needs attention across your workspace</span>
        {hasItems ? <span className="focus-count">{focus.items.length}</span> : null}
      </div>

      {hasItems ? (
        <div>
          {focus.items.map((item) => (
            <a className={`dcfocus-row sev-${item.severity}`} href={item.url} key={`${item.kind}:${item.projectId}:${item.title}`}>
              <span className="dcfocus-box" aria-hidden="true" />
              <span className="min-w-0">
                <span className="dcfocus-title">{item.title}</span>
                <span className="dcfocus-detail">{item.detail}</span>
              </span>
              <span className="dcfocus-proj">{item.project}</span>
            </a>
          ))}
        </div>
      ) : (
        <div className="dcfocus-clear">✓ All clear — nothing needs attention right now.</div>
      )}
    </section>
  )
}
