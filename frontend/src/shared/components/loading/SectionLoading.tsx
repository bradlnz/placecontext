const SKELETON_CARDS = ['one', 'two', 'three', 'four', 'five', 'six'] as const

export function SectionLoading() {
  return (
    <div className="section-loading" role="status" aria-label="Loading workspace">
      <div className="skeleton skeleton--heading" />
      <div className="skeleton-stats">
        {SKELETON_CARDS.slice(0, 4).map((key) => (
          <div className="skeleton skeleton--stat" key={key} />
        ))}
      </div>
      <div className="skeleton skeleton--panel" />
      <div className="skeleton-grid">
        {SKELETON_CARDS.map((key) => (
          <div className="skeleton skeleton--card" key={key} />
        ))}
      </div>
      <span className="sr-only">Loading workspace overview…</span>
    </div>
  )
}
