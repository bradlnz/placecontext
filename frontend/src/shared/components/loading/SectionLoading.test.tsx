import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'

import { SectionLoading } from './SectionLoading'

describe('SectionLoading', () => {
  it('announces loading and renders layout-shaped placeholders', () => {
    const { container } = render(<SectionLoading />)

    expect(screen.getByRole('status', { name: 'Loading workspace' })).toBeVisible()
    expect(screen.getByText('Loading workspace overview…')).toHaveClass('sr-only')
    expect(container.querySelectorAll('.skeleton--stat')).toHaveLength(4)
    expect(container.querySelectorAll('.skeleton--card')).toHaveLength(6)
  })
})
