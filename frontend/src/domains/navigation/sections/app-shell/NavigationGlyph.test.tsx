import { render } from '@testing-library/react'
import { describe, expect, it } from 'vitest'

import { NavigationGlyph, type NavigationIcon } from './NavigationGlyph'

const HOST_NAVIGATION_ICONS: readonly NavigationIcon[] = [
  'grid',
  'crm',
  'box',
  'test',
  'chain',
  'clock',
  'data',
  'key',
  'pulse',
  'chat',
  'file',
  'ledger',
]

describe('NavigationGlyph', () => {
  it.each(HOST_NAVIGATION_ICONS)('renders the Host %s icon as decorative SVG', (kind) => {
    const { container } = render(<NavigationGlyph kind={kind} />)

    expect(container.querySelector('svg')).toHaveAttribute('aria-hidden', 'true')
    expect(container.querySelector('svg path, svg rect, svg circle')).not.toBeNull()
  })
})
