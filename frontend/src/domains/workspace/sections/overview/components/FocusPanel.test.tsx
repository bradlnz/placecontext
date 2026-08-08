import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'

import { workspaceOverviewFixture } from '../../../../../test/fixtures/workspace'
import { FocusPanel } from './FocusPanel'

describe('FocusPanel', () => {
  it('renders actionable focus items as legacy links', () => {
    render(<FocusPanel focus={workspaceOverviewFixture.focus} />)

    expect(screen.getByRole('link', { name: /re-index atlas/i })).toHaveAttribute(
      'href',
      `/project/${workspaceOverviewFixture.projects[0]?.id ?? ''}`,
    )
    expect(screen.getByText('Atlas')).toBeVisible()
    expect(screen.getByText('1')).toHaveClass('focus-count')
  })

  it('renders an explicit all-clear state', () => {
    render(<FocusPanel focus={{ items: [], projectCount: 4 }} />)

    expect(screen.getByText(/All clear/)).toBeVisible()
    expect(screen.queryByText('4')).not.toBeInTheDocument()
  })
})
