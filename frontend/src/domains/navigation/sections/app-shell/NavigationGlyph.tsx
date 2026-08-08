export type NavigationIcon =
  | 'grid'
  | 'crm'
  | 'box'
  | 'test'
  | 'chain'
  | 'clock'
  | 'data'
  | 'key'
  | 'pulse'
  | 'chat'
  | 'file'
  | 'ledger'

export function NavigationGlyph({ kind }: { kind: NavigationIcon }) {
  const commonProps = {
    viewBox: '0 0 24 24',
    fill: 'none',
    stroke: 'currentColor',
    strokeWidth: 1.7,
    strokeLinecap: 'round' as const,
    strokeLinejoin: 'round' as const,
    'aria-hidden': true,
  }

  switch (kind) {
    case 'grid':
      return (
        <svg {...commonProps}>
          <rect x="3" y="3" width="7" height="7" rx="1.5" />
          <rect x="14" y="3" width="7" height="7" rx="1.5" />
          <rect x="14" y="14" width="7" height="7" rx="1.5" />
          <rect x="3" y="14" width="7" height="7" rx="1.5" />
        </svg>
      )
    case 'crm':
      return (
        <svg {...commonProps}>
          <path d="M12 13.5c3 0 5.5 2.5 5.5 5.5v1H6.5v-1c0-3 2.5-5.5 5.5-5.5Z" />
          <circle cx="12" cy="8" r="3.2" />
        </svg>
      )
    case 'box':
      return (
        <svg {...commonProps}>
          <path d="M4 7l8-4 8 4-8 4-8-4Z" />
          <path d="M4 7v10l8 4 8-4V7" />
          <path d="M12 11v10" />
        </svg>
      )
    case 'test':
      return (
        <svg {...commonProps}>
          <path d="M9 2h6M10 22h4M12 9v3M9 6 6 9l3 3M15 6l3 3-3 3M12 14v7M6 12h12" />
        </svg>
      )
    case 'chain':
      return (
        <svg {...commonProps}>
          <path d="M4 12h3.5M9 12h6M16.5 12H20M8 9h8V5M12 15v5m0-5 4-2.5m0 0 4 2.5m-4-2.5L12 15M6.5 8.5h3m-3 7h3M14.5 9h3m-3 6h3" />
        </svg>
      )
    case 'clock':
      return (
        <svg {...commonProps}>
          <circle cx="12" cy="12" r="9" />
          <polyline points="12 7 12 12 15 15" />
        </svg>
      )
    case 'data':
      return (
        <svg {...commonProps} strokeWidth="1.8">
          <path d="M4 7h16M4 11h16M4 15h16M4 19h16" />
        </svg>
      )
    case 'key':
      return (
        <svg {...commonProps}>
          <circle cx="7.5" cy="16.5" r="2.5" />
          <path d="M10 16.5h9.5M19.5 16.5l-1.1-1.1-2.2-2.2-1.8-1.8M16.5 13.5 14 11a3 3 0 1 0-4.2 4.2" />
        </svg>
      )
    case 'pulse':
      return (
        <svg {...commonProps}>
          <path d="M3 12h4l2-4 3 8 2-4 3 4h7" />
        </svg>
      )
    case 'chat':
      return (
        <svg {...commonProps}>
          <path d="M21 15a4 4 0 0 1-4 4H7l-4 3V4a4 4 0 0 1 4-4h10a4 4 0 0 1 4 4Z" />
        </svg>
      )
    case 'file':
      return (
        <svg {...commonProps}>
          <path d="M14 2H7.5A1.5 1.5 0 0 0 6 3.5v17A1.5 1.5 0 0 0 7.5 22h9a1.5 1.5 0 0 0 1.5-1.5V8Z" />
          <path d="M14 2v6h6M8 10h8M8 14h8M8 18h6" />
        </svg>
      )
    case 'ledger':
      return (
        <svg {...commonProps}>
          <path d="M4 4h16M4 8h16M4 12h16M4 16h16M4 20h16M8 4v16M16 4v16" />
        </svg>
      )
  }
}
